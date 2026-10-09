# Phase 9b — Confirmation & Probation Scheduler + Notifications

## Goal

Build on the Phase 9a policy foundation to **proactively raise confirmation-review reminders** before an
employee's probation completes. A background worker scans each tenant for employees approaching their
policy-derived probation end and notifies the **employee, their reporting/L1 manager, and the tenant's
HRBPs** — once per recipient per probation cycle, idempotently, with retry and a durable execution log.

9b is strictly **notify-only**: it never writes an employee's Confirmation Date or Job Status, never records
a decision, rating or review answer. Those belong to Phase 9c.

## Design decisions

- **Mirror the leave reminder worker.** `ConfirmationReminderWorker` + `ConfirmationReminderProcessor` +
  `ConfirmationReminderNotificationService` follow `LeaveApprovalReminderWorker` /
  `LeaveApprovalReminderProcessor` / `LeaveNotificationService` exactly: a `PeriodicTimer` host loop,
  per-tenant shard scopes, and a claim/lease/dedup row per occurrence. One proven concurrency model.
- **One durable row per (employee, probation cycle, recipient).** `ConfirmationReminderDelivery` is both the
  atomic claim-and-dedup unit and the **execution log**. The probation cycle is identified by the
  policy-derived `ProbationEndDate`, so a probation extension (which moves the end date, Phase 9c) is a new
  occurrence and re-notifies, while the same end date never re-notifies a recipient already `Sent`. The
  unique `(TenantId, OccurrenceKey)` index is what makes the claim safe across workers.
- **Policy window is authoritative; tenant setting is the fallback.** The trigger window comes from the
  applicable `ConfirmationPolicyRule.TriggerDaysBeforeEnd` (9a); a tenant's `DefaultTriggerDaysBeforeEnd`
  is used only when the policy leaves it at zero. The review window is `[ReviewTriggerDate, ProbationEndDate]`.
- **Candidates are active, unconfirmed employees.** The scan excludes confirmed employees
  (`EmployeeEmployment.ConfirmationDate` set) and inactive/terminated employees up front, then resolves the
  9a policy per candidate; employees with no applicable policy are skipped (manual fallback is unchanged).
- **HRBP is resolved tenant-wide.** HRBP is a role, not a per-employee link, so 9b notifies the tenant's
  current HRBP-role holders (account-linked, effective today). Org-scoped HRBP targeting is a 9c refinement.
  Missing employee / manager / HRBP recipients are logged and omitted, never fatal.

## Data model (tenant chains)

| Table | Purpose |
|---|---|
| `ConfirmationReminderDeliveries` | Claim/lease/dedup + delivery history + execution log: tenant, employee, policy + version, probation end, trigger date, recipient type/id/email, occurrence key, status, attempts, lease/claim token, sent/processed/next-attempt timestamps, last error. |
| `ConfirmationSchedulerSettings` | One row per tenant: enabled, scan frequency, default trigger days, max attempts / lease overrides, resend toggle, per-recipient toggles, `LastRunAtUtc`. |

Composite `(TenantId, Id)` alternate keys and tenant-owned composite FKs; EF global query filters + the
SaveChanges tenant guard apply as for every tenant entity. Migrations added to **both** tenant chains
(`HRMS.Infrastructure/Persistence/Migrations`, `HRMS.Infrastructure.MySqlMigrations`) with their snapshots;
the catalog chain is unaffected.

## Processing (`ConfirmationReminderProcessor.ProcessAsync`, per tenant scope)

1. Load the tenant setting. Skip if disabled or not yet due (`LastRunAtUtc + FrequencyMinutes > now`).
2. Query active, unconfirmed employees (bounded by `BatchSize`).
3. For each: resolve the 9a policy on today; skip if none/ambiguous. Compute probation end and the effective
   trigger window; keep only employees inside `[trigger, probationEnd]`.
4. Resolve recipients (employee / manager / HRBP, filtered by the tenant toggles); skip-and-log if none.
5. For each recipient: build the occurrence key, **claim** (insert-or-lease-takeover via the unique index),
   **send**, then **complete** (`Sent`/`Skipped`) or **fail** (`Failed` + `NextAttemptAtUtc`, bounded by
   `MaxAttempts`). A recipient already `Sent` is never re-sent unless `ResendProcessed` is enabled.
6. Stamp `LastRunAtUtc`.

## Configuration

Host-wide `ConfirmationReminders` options (master `Enabled` off by default, worker tick, batch size, default
max attempts / lease / backoff / trigger days). Per-tenant `ConfirmationSchedulerSettings` override cadence
and delivery policy via `ConfirmationSchedulerController` (`/api/confirmation-scheduler`).

## API & authorization

`ConfirmationSchedulerController`: `GET/PUT settings`, `GET executions`. New permissions
`Confirmation.SchedulerView` / `Confirmation.SchedulerManage` (seeded: TenantAdmin all; HRAdmin view+manage;
HRManager view), mirrored in the frontend `permissions.ts` (parity test enforces name + order).

## Frontend

`src/pages/confirmation/ConfirmationSchedulerPage` — settings form + execution-log table, routed at
`/confirmation/scheduler`, nav entry under Employee Management gated by `Confirmation.SchedulerView`.

## Tests

- Backend `ConfirmationReminderTests` (14): window/candidacy, no-policy skip, confirmed/inactive exclusion,
  dedup, retry/backoff, max-attempts, no-recipient skip, distinct recipients, multi-tenant isolation,
  disabled/frequency gates, and real recipient resolution (employee/manager/HRBP) incl. missing recipients.
- Frontend `ConfirmationSchedulerPage.test.tsx` + the permissions parity test.
- QA `qa/18-confirmation-probation/cases/02-confirmation-scheduler.yaml`,
  `qa/automation/tests/api/test_confirmation_scheduler_smoke.py` (+ `core/confirmation_api.py` helpers).

SQLite proves the logic; run the MySQL/SQL Server provider suites for locking parity before release.

## Deferred to Phase 9c

Dynamic review-question master, rating scale, review responses + audit, configurable approvals with manual
outcome, **auto-confirmation** (writing `ConfirmationDate` / `JobStatus` with audit + probation-extension
recalculation), and the review-form / dashboard screens. Org-scoped HRBP targeting for reminders.
