# Phase 7Q — Bonus, Incentives & Variable Pay

## Scope

Phase 7Q adds tenant-scoped variable-pay plans, effective-dated published versions, auditable awards, maker-checker approval, settlement records, tenant/year award numbering, and ESS/admin surfaces. Configuration is authoritative: salary basis, percentages, target values, performance multiplier bounds, proration, payout frequency, tax treatment, and Final Settlement treatment are persisted with the plan version and award snapshot.

Supported bounded methods are fixed amount, percentage of configured salary basis, target percentage multiplied by an authorized performance multiplier, and controlled manual amount. The implementation deliberately does not provide arbitrary formula scripting, a performance-management engine, or a commission-tier engine.

## Lifecycle and controls

Awards move through Draft/Calculated, Submitted, Approved or Rejected, Scheduled, PartiallyPaid/Paid, and Cancelled states. Award numbers use the tenant/year format `VPA/YYYY/000001`; the sequence is persisted per tenant and year and award uniqueness is also enforced by the employee/plan-version/period key.

Approval, override, cancellation, settlement, and history operations are permission-controlled. Self-approval is rejected when the submitting identity matches the approving identity. Calculated, approved, taxable, non-taxable, settled, plan, and plan-version values remain traceable in the award and immutable history records.

## Settlement and accounting foundation

Payroll and Final Settlement integrations consume persisted award outcomes rather than recalculating plan eligibility. `VariablePaySettlement` records settlement type, amount, tax split, source references, and the optional PayrollRun, PayrollResult, or FinalSettlement link. Settlement updates are guarded by prior status/outstanding values and duplicate source uniqueness. Accounting uses configured mapping sources; no GL account or statutory bonus rule is hardcoded in Phase 7Q.

## UI and reporting

Admin can view variable-pay plans and award register data. ESS can view awards linked to the authenticated employee identity. Register queries are paginated and expose calculated, approved, paid, outstanding, taxable, and non-taxable amounts.

## Provider and regression verification

`PayrollVariablePayProviderAcceptance` is the shared acceptance path used by `SqlServerPayrollVariablePayIntegrationTests` and `MySqlPayrollVariablePayIntegrationTests`. It covers plan/version persistence, award generation and numbering, approval, tax split, settlement, Final Settlement linkage, tenant isolation, and cleanup/repeatability. SQL Server operator runtime is intentionally the final external gate; it is not run during implementation work.

## Deferred

Full Performance Management, KPI/OKR calculation, complex sales commissions, retroactive commission adjustments, ESOP/RSU/equity compensation, deferred and long-term incentives, arbitrary formula scripting, external performance/commission engines, live banking, and jurisdiction-specific bonus-law automation remain deferred.

## Final closure verification

Provider acceptance is complete on both relational providers. `SqlServerPayrollVariablePayIntegrationTests` passed 1/1 on the first run and 1/1 on same-database repeatability; both runs had 0 failures and 0 skips. Durations were not included in the operator evidence supplied for closure. `MySqlPayrollVariablePayIntegrationTests` passed 1/1 on the first run and 1/1 on repeatability, with no provider issues.

Both provider classes invoke `PayrollVariablePayProviderAcceptance.RunAsync`. The shared path covers the plan, effective-dated version, tenant/year award numbering, eligibility, salary basis, award generation, maker-checker approval, tax split, Payroll settlement, `VariablePaySettlement` persistence, representative Final Settlement behavior, accounting, register/query behavior, tenant isolation, cross-tenant denial, cleanup, and repeatability. No SQL Server-specific business workaround exists; SQL Server/MySQL provider parity is PASS.

Historical awards retain the plan and plan-version identifiers, salary-basis snapshot, proration factor, calculated amount, approved amount, and taxable/non-taxable split. Payroll consumes persisted approved award values. Payroll and Final Settlement settlement paths use persisted outstanding amounts and source-linked settlement records, preventing duplicate payment of the same outstanding award. Award numbering is tenant-scoped, year-scoped, unique, and protected by persisted sequence/idempotency constraints.

Final verification evidence:

- Phase 7Q focused: 6 passed, 0 failed, 1 expected SQL Server skip.
- Payroll fast: 97 passed, 0 failed, 0 skipped.
- Non-provider backend: 1,150 passed, 0 failed, 0 skipped; provider tests excluded. The known AttendanceReport runner delay remains documented separately.
- Frontend focused: 2 passed, 0 failed, 0 skipped.
- Frontend full: 602 passed, 0 failed, 0 skipped.
- Backend and solution builds, TypeScript, production build, and lint passed; lint has existing warnings.
- SQL Server and MySQL Phase 7Q migrations and snapshots are synchronized, with no pending model changes; Phase 7A–7P migrations remain unchanged.
- No hardcoded bonus percentage, service threshold, performance multiplier, payout period, cap, tax rule, separation treatment, or salary-component dependency was introduced.

Final status: **PHASE 7Q BONUS, INCENTIVES & VARIABLE PAY: COMPLETE**
