# Phase 7P — Gratuity & Advanced Separation Benefits

## Scope and architecture

Phase 7P adds tenant-scoped, effective-dated gratuity policy and version records, immutable calculation snapshots, controlled overrides, separation-benefit history, notice settlement calculations, and leave encashment calculations sourced only from the existing authoritative employee leave balance. It integrates with the existing Final Settlement lifecycle and accounting model; it does not add a payment gateway or a legal/tax rules engine.

Policy versions determine eligibility, separation-reason applicability, service rounding, formula, wage basis, caps, monetary rounding, and taxable/non-taxable treatment. No statutory factor, wage component code, exemption, or legal threshold is hardcoded.

## Calculation and settlement

The service resolves joining and last-working dates from authoritative employment data, selects the effective published policy version, snapshots service length and wage inputs, calculates the configured formula using decimal arithmetic, applies configured rounding/caps/minimums, and persists the taxable/non-taxable split. A fixed-amount policy does not require a salary assignment; other wage bases require an effective salary assignment and configured component selection.

Eligible gratuity, leave encashment, and notice pay/recovery are represented as explicit Final Settlement source lines. Finalization locks the gratuity snapshot and is retry-safe. Existing Final Settlement transition semantics remain authoritative. Leave encashment is bounded to existing `EmployeeLeaveBalance.AvailableQuantity`; a new leave-policy engine is intentionally deferred.

Accounting uses configuration-driven GL mappings for gratuity, leave encashment, notice pay/recovery, and the Final Settlement benefit payable. Missing mappings fail before journal persistence, and benefit source IDs remain traceable.

## Security and UI

New permissions cover viewing, calculating, policy management, override, override approval, and history. Admin endpoints expose policy/version management, preview, calculation, register, and controlled override actions. ESS reads the linked employee identity and exposes the employee’s own separation-benefit summary only. Tenant query filters and composite tenant foreign keys protect persisted data.

The frontend adds Payroll Separation Benefits (policy/register/detail/history) and My Separation Benefits views with policy/version, service, gratuity, tax split, leave, notice, and Final Settlement status information. Detail and history calls are tenant-scoped and use the Final Settlement identifier captured by the register. The API also exposes policy history for version/audit review.

## Verification plan

The Phase 7P test coverage includes policy/version validation, effective-date selection, service and formula boundaries, caps/rounding, tax split, Final Settlement snapshot/linkage, idempotent finalization, tenant isolation, focused frontend rendering, a concurrency/idempotency suite, bounded large-data reconciliation, and shared SQL Server/MySQL provider acceptance. Provider migrations are generated independently for SQL Server and MySQL with synchronized snapshots.

Final SQL Server operator runtime remains the last external verification step. MySQL provider acceptance must pass on first run and repeatability before that step.

## Current verification record

- Phase 7P focused backend: 6 passed, 1 expected SQL Server skip, 0 failed.
- Broad backend regression excluding provider categories and the previously classified `AttendanceReportTests` runner-delay class: 1,338 passed, 0 failed, 19 expected SQL Server skips.
- Phase 7P large-data acceptance: 100 separation calculations, 100 persisted snapshots, INR 100,000 gratuity reconciliation, tenant isolation passed.
- Payroll fast: 92 passed, 0 failed, 0 skipped.
- MySQL provider acceptance: final-code first run passed 1/1 in approximately 29 seconds; repeatability passed 1/1 in approximately 29 seconds.
- Full frontend: 600 passed, 0 failed, 0 skipped; Phase 7P focused frontend: 2 passed, 0 failed, 0 skipped.
- TypeScript and production build passed. Lint passed with pre-existing warnings.
- Backend and solution builds passed. Both SQL Server and MySQL report no pending model changes; Phase 7P migrations and snapshots are synchronized.
- SQL Server provider acceptance: first operator run passed 1/1, 0 failed, 0 skipped; the supplied operator record did not include a duration.
- SQL Server same-database repeatability: second operator run passed 1/1, 0 failed, 0 skipped; the supplied operator record did not include a duration.
- SQL Server and MySQL both execute the shared `PayrollSeparationBenefitsProviderAcceptance.RunAsync` path. No SQL Server-specific business workaround was required, and provider parity passed across policy/version persistence, separation data, eligibility, service length, wage basis, calculation snapshots, caps, rounding, tax split, Final Settlement, notice pay/recovery, authoritative leave encashment, representative override/accounting paths, register queries, tenant isolation, cross-tenant denial, cleanup, and repeatability.
- Final Settlement consumes the persisted gratuity calculation snapshot; finalized gratuity is not recalculated from a later policy version. Leave encashment uses authoritative leave balances with duplicate-protection constraints, and notice pay/recovery uses authoritative employment notice data with policy-configured inputs.
- Phase 7P closure status: complete. No pending model changes were reported; Phase 7P SQL Server/MySQL migrations and snapshots are synchronized, and Phase 7A–7O migrations remain unchanged.

## Deferred scope

Deferred: actuarial valuation and liability provisioning, jurisdiction-specific legal advice, pension/retiral fund withdrawal, insurance settlement, government filing, live payments, ERP integration, arbitrary formula scripting, and a full leave-encashment policy engine where authoritative leave balance/eligibility is unavailable.
