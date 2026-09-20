# Phase 7J — Retro / Arrears and Final Settlement Foundation

Phase 7J adds separate, tenant-scoped aggregates for retro cases, historical difference results, explicit payroll adjustments, and final settlement cases. Existing finalized payroll results are source records only: retro evaluation never overwrites them.

## Retro

`PayrollRetroCase` records a deterministic trigger and effective window. Evaluation reads current historical payroll snapshots and persists `PayrollRetroResult` and component-level differences. Positive and negative differences retain their sign. Approved cases can create explicit `PayrollAdjustment` rows for a draft/prepared future run; source and target references prevent the adjustment from becoming an implicit salary mutation.

The current foundation deliberately treats a case with no corrected difference as `NoImpact`. Segmented retro recalculation, statutory legal retro rules, and automatic detection from every master change remain deferred.

## Final settlement

`FinalSettlementCase` snapshots employee identity and separation dates. `FinalSettlementLine` is the only calculation input: earning and deduction lines are rounded with decimal `AwayFromZero` policy, then totalled into gross payable, deductions, and net settlement. The lifecycle is Draft → Calculated → Approved → Finalized. Finalized cases and lines are immutable, and duplicate active cases for the same employee/separation are rejected.

Leave encashment, notice recovery, arrears, and other settlement sources are represented by explicit line/source types; no unsupported legal or statutory calculation is inferred. Payment and accounting integrations remain source-reference extension points and do not submit funds or post external journals.

## Security and persistence

All entities have tenant-scoped query filters, composite tenant foreign keys, restrictive source/reference deletes, and cascade only from an owned case to its lines/history/results. SQL Server migration generation therefore avoids multiple cascade paths. Provider migrations are maintained separately for SQL Server and MySQL; Phase 7A–7I migrations are unchanged.

The service rejects cross-tenant employees, runs, retro cases, and settlements through the tenant context. History is append-only for lifecycle events, and historical payroll rows are never updated by retro evaluation.

## APIs and permissions

Retro case creation/evaluation/approval/application is exposed under `/api/payroll/retro/cases`. Final settlement creation, line entry, calculation, approval, and finalization is exposed under `/api/payroll/final-settlements`. Dedicated `Payroll.Retro.*` and `Payroll.FinalSettlement.*` permissions protect each operation.

## Verification

Focused SQLite coverage covers no-impact retro evaluation, settlement line calculation, duplicate settlement protection, finalization immutability, and tenant isolation. The fast Payroll regression is green at 38 passed, 0 failed, and 0 skipped. The focused frontend coverage contains two Phase 7J page-render tests; the full frontend suite is green at 587 passed, with TypeScript and the production build passing. Lint remains green with existing warnings.

Provider acceptance classes are `SqlServerPayrollRetroSettlementIntegrationTests` and `MySqlPayrollRetroSettlementIntegrationTests`; both use provider-specific migrations and repeatable test-owned data. The MySQL provider acceptance executed twice successfully, 1/1 each run, and covered migration application, settlement persistence/lifecycle, retro persistence/evaluation, and tenant-scoped provider behavior. SQL Server operator verification also completed successfully twice against the same integration database, 1/1 each run, with no provider issues.

```powershell
$env:HRMS_SQLSERVER_TEST_CONNECTION = "<dedicated disposable SQL Server connection string>"
dotnet test Backend\HRMS.Tests\HRMS.Tests.csproj `
  --filter "FullyQualifiedName~SqlServerPayrollRetroSettlementIntegrationTests" `
  --logger "console;verbosity=normal"
```

The provider migrations are `AddPayrollRetroArrearsFinalSettlementV2` for SQL Server and MySQL. They were regenerated from the Phase 7I snapshots after correcting an incomplete migration delta; the migrations create the complete Phase 7J schema. Backend and solution builds pass with zero errors, and `git diff --check` passes.

## Deferred

- full gratuity/statutory final-settlement rules
- automatic segmented retro recalculation
- loan and reimbursement engines
- live settlement payment
- ERP integration and statutory filing

Segmented or multi-segment historical retro recalculation is explicitly deferred; this phase provides the retro case, historical comparison foundation, and explicit adjustment model without claiming full segmented recalculation. Full gratuity/statutory settlement rules, loan/reimbursement engines, live settlement payment, ERP integration, and statutory filing are also deferred. No commit or push has been performed.
