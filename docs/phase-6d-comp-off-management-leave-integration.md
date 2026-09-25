# Phase 6D — Comp-Off Management & Leave Integration

## Status

The Phase 6D foundation is implemented as an uncommitted continuation of the
Phase 6C baseline. The current implementation is intentionally bounded: the
policy, earning, immutable ledger, balance, expiry, correction, and Leave
accounting seams are present and covered by focused SQLite acceptance tests.
Provider acceptance, the complete enterprise concurrency/failure matrix,
large-data acceptance, and the complete operational UI remain closure work.
Phase 6E is deferred.

## Architecture

Authoritative Attendance is the source of worked minutes. `CompOffService`
resolves the tenant policy effective on the source work date, validates the
Attendance day and source category, calculates credited minutes, and records a
versioned earning. Approved earnings create an append-only credit ledger entry.
Balances are derived from ledger entries rather than stored as an editable
counter.

Comp-Off Leave uses the existing Leave request lifecycle. Submission reserves
credit, approval consumes the reservation, rejection releases it, and an
allowed cancellation restores it. The integration is deliberately narrow and
does not create a second Leave workflow.

## Policy and calculation

Policies are tenant-scoped and effective-dated. Source work resolves the policy
whose effective range contains the work date. The policy controls source
categories, eligibility mode, minimum worked minutes, credit ratio, rounding,
daily/monthly caps, approval, expiry, partial-day behavior, and benefit mode.
Credit is stored in integer minutes. Display conversion to hours or days is a
presentation concern.

Daily and monthly caps are applied to credited minutes; raw and eligible source
minutes remain on the earning for audit. The current focused acceptance suite
verifies both caps and the month-wide accumulation behavior.

Week-Off and Holiday are supported source categories. Incomplete or unfinalized
Attendance is not treated as a spendable source. An employee ineligible under
the policy receives no silent credit. Normal-day late stay is not automatically
converted into Comp-Off.

## Ledger, balance, and expiry

Ledger entries are append-only and include tenant, employee, earning, optional
Leave request, entry type, minutes, effective date, expiry, source reference,
and an idempotency key. The balance exposes earned, available, reserved,
consumed, and expired minutes. Reservation selects earliest expiry first and
then oldest source date, with ID as a deterministic tie-breaker.

Expiry is persisted on the earning at creation time. Expiry emits an `Expire`
ledger entry and retains the earning in history. Derived state treats
Reserve→Consume as one transition: the historical Reserve and Consume entries
are both retained, but the reservation is not subtracted twice. A restore after
expiry is retained as an auditable Restore entry and remains expired/non-
spendable; it does not extend the original expiry date.

## Leave integration

Leave types carry an explicit `IsCompOff` classification. Comp-Off Leave uses
the existing submission, approval, rejection, and cancellation services. The
current integration maps one Leave day to the repository's minute-based
consumption unit and keeps the reservation/consumption operations idempotent.
Multiple earning buckets can be allocated to one request, and an allocation
records reserved, consumed, and released minutes.

## Corrections and traceability

An Attendance correction does not rewrite an earning. It supersedes the old
earning, creates a replacement, and emits a reversal or incremental credit as
appropriate. Source Attendance day, source Attendance version, policy, and
optional Overtime request/snapshot references are retained. A correction that
would reduce credit below already-consumed minutes is rejected for controlled
operational handling rather than creating a silent negative balance.

An increase creates only the net incremental ledger credit. A reduction after
consumption returns `CorrectionRequiresAdjustment`; historical consumption is
preserved and no silent negative balance is created. Full consumed-credit
operational adjustment workflow remains an enterprise acceptance gap.

## Monetary overtime exclusivity

Phase 6C remains the owner of monetary overtime. Comp-Off does not calculate
money and does not modify Payroll overtime snapshots. An Overtime source is
rejected for Comp-Off unless the configured benefit mode explicitly allows the
combination; the current safe default is non-dual benefit. The reciprocal guard
is enforced in the real `OvertimeService.CreateRequestAsync` path before an OT
request is persisted: a tenant/employee Attendance-day source already committed
to a non-rejected, non-superseded Comp-Off earning returns
`DoubleBenefitDenied`. Rejected and superseded Comp-Off history does not reserve
the source by itself.

The source identity is tenant plus employee plus the authoritative
`EmployeeAttendanceDay.Id`. Comp-Off additionally retains its
`SourceAttendanceVersion`; finalized monetary OT retains its overtime snapshot
and Attendance snapshot/version references. This distinguishes separate work
days/events for the same employee and leaves historical OT and Comp-Off records
immutable when the incompatible second benefit is rejected.

## Authorization and provider parity

Comp-Off self-service reads use the employee identity resolved from the caller.
Operational endpoints are permission protected with the seeded Comp-Off
permissions, while tenant query filters remain authoritative for isolation.
SQL Server and MySQL migrations are generated as new Phase 6D migrations; no
historical Attendance, Phase 6B, Phase 6C, Payroll, or Phase 8 migrations are
modified.

The SQL Server model check is clean from the current evidence. MySQL has prior
clean evidence, but a fresh design-time validation is currently blocked because
the MySQL design-time connection is unavailable. The final Phase 6D migration
pair also removes the unintended shadow tenant relationships from the initial
Comp-Off mapping.

## Verification status and limitations

The non-provider Phase 6D acceptance manifest contains 59 unique tests: 59
executed and 59 passed. This includes 28 core tests, 10 Leave integration
tests, balance invariants, correction and consumed-credit correction tests,
five reciprocal OT/Comp-Off tests, 10 authorization tests, eight real race
tests, six failure-injection tests, the Attendance-to-Comp-Off-to-Leave E2E,
and the 10,000-employee large-data test. The large-data fixture uses database
side count/order/skip/take paging with page size 100 and verifies zero duplicate
source credits, over-reservations, negative balances, duplicate consumption or
restoration, and tenant leakage.

The measured large-data run used 10,000 employees, 5,000 source events, 5,000
credits, 2,000 Leave requests, 1,500 reserved and consumed minutes, 5,000
expired credits, and 500 restored minutes. Credit generation took 133.6 ms,
balance resolution 41.4 ms, first-page retrieval 14.1 ms, and reservation 4.1
ms; the test-runner duration was 14 seconds.

The employee UI displays backend-calculated balance, earning, expiry, ledger,
and correction information. The existing Leave screen remains the Comp-Off
Leave entry point. Operational users use the separate
`GET /api/attendance/comp-off/operations` query, which applies the existing
Attendance authorization predicate, tenant boundary, filters, count, stable
ordering, and database-side paging before materializing a page. The response
includes employee identity, source/policy/Attendance traceability, expiry,
approval state, and per-earning balance values. Focused operational-query
coverage is 10/10 and focused UI coverage is 17/17; the full frontend suite
retains one unrelated pre-existing Dashboard assertion failure.

Provider wrappers discover one MySQL and one SQL Server test. Fresh MySQL model
validation reports no pending model changes, and both MySQL acceptance runs
pass, including source-credit idempotency. SQL Server discovery succeeds, but
execution is pending until the operator supplies
`HRMS_SQLSERVER_TEST_CONNECTION`; no SQL Server result is inferred without that
connection.

The final Attendance, Leave, Payroll/OT, and bounded non-provider regressions
remain green after the operational query change. MySQL provider evidence is
retained because the provider acceptance path and schema were unchanged. Phase
6D is not marked complete until both SQL Server provider runs are green.
