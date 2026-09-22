# Phase 7U — Employee Tax Declarations & Investment Proofs

## Scope

Phase 7U adds a tenant-scoped, read/write declaration workflow for employee tax declarations and investment-proof metadata. It owns declaration cycles, configurable categories/items, employee lines, proof metadata, review decisions, partial approval, resubmission, locking/reopening, audit events, and an approved-input resolver for Payroll.

The feature is not a tax calculation engine. It does not encode statutory rates or limits, recalculate annual tax, mutate finalized Payroll, or rewrite previously approved evidence.

## Workflow

Cycles are configurable by financial year and effective dates. A cycle can be Draft, Open, ProofSubmissionOpen, ReviewInProgress, Locked, Closed, or Cancelled. Employees create one declaration per cycle, add lines, attach proof metadata, and submit. Reviewers can approve, partially approve, reject, or request resubmission. Approved amounts remain separate from declared amounts; Payroll consumes only approved amounts. Lock and reopen actions are audited.

Proofs store document metadata and an opaque storage reference. File contents are not stored in the tax declaration tables and physical storage paths are not exposed by the DTOs.

## Payroll handoff

`ITaxDeclarationService.ResolveApprovedAsync` returns approved declaration items, approved amounts, source references, declaration version, and approval time for a tenant, employee, financial year, and effective date. It does not calculate tax. A partial approval of 100,000 declared and 75,000 approved therefore exposes 75,000 only. Finalized Payroll remains unchanged if a declaration is later reopened or revised.

## Security and tenancy

Employee self-service resolves the employee only through the existing account-to-employee current-link resolver. It does not infer identity from email, username, or matching IDs. All cycle, category, item, declaration, line, proof, audit, review, and resolver queries are tenant-scoped. Configuration, review, approval, reopen, and audit access use dedicated canonical Payroll permissions.

## Persistence

Provider migrations were added without modifying Phase 7A–7T migrations:

- SQL Server: `20260922144643_AddPayrollTaxDeclarationsPhase7U`
- MySQL: `20260922144704_AddPayrollTaxDeclarationsPhase7U`

The SQL Server and MySQL model snapshots include equivalent Phase 7U entities. No schema is used for tax calculation formulas.

## API and employee surface

Configuration, employee self-service, review, approval, resubmission, locking/reopening, audit, draft line update/delete, proof review, and rejected-proof replacement routes are exposed through `TaxDeclarationsController` and `MyTaxDeclarationsController`. Replacement creates new proof metadata and marks the rejected record as replaced; it does not overwrite historical evidence. The frontend `Payroll / My Tax Declarations` surface now exposes draft edit/delete, rejected-proof replacement, reviewer comments, and explicit resubmission controls. Upload and review commands remain permission-bound backend operations.

## Verification status

Dedicated backend suites now pass independently: self-service 1/1, reviewer 1/1, proof 1/1, and resubmission 1/1 (combined 4/4). The focused workflow test passes 1/1 and covers draft line update/delete, required-proof blocking, proof rejection, resubmission, non-destructive rejected-proof replacement, proof acceptance, partial approval, and approved-only resolution. The named seven-scenario optimistic-concurrency matrix executes 8/8 tests; the workflow/large-data/concurrency group is 10/10. The large-data case covers 1,000 employees, mixed declaration statuses, proof metadata, reviewer paging, approved-only resolution, and tenant isolation. Payroll-fast passes 138/138.

The frontend focused Phase 7U suite passes 4/4. The full frontend suite passes 87 files and 609 tests outside the restricted sandbox. TypeScript compilation passes, the production build passes outside the sandbox, and lint completes with existing repository warnings only. The earlier Vite `spawn EPERM` was therefore classified as a sandbox/process-launch restriction rather than an application failure.

Backend and solution builds pass, and `git diff --check` passes. The bounded non-provider backend regression excludes only provider-named tests and passes 1,228/1,228 in 3m35s. MySQL provider acceptance passes 1/1 in 50s after the workflow additions. SQL Server operator reverification passed on the same disposable database: first run 1/1 with test time 20.4605s and overall duration 20.8s; repeat run with `--no-build` 1/1 with test time 9.2162s and overall duration 9.4s. The SQL Server wrapper and MySQL wrapper both call `PayrollTaxDeclarationsProviderAcceptance.RunAsync`, with no provider-specific business workaround. The frontend exposes the supported draft/resubmission controls; reviewer/configuration UI remains outside this employee-surface checkpoint.

Final provider status: FINAL OPERATOR REVERIFICATION PASSED. SQL Server pending model changes are None, MySQL pending model changes are None, snapshots are synchronized, and no Phase 7A–7T migration was modified.

## Deferred

External tax portals, government filing, OCR, AI proof validation, fraud scoring, predictive tax optimization, automatic legal advice, and arbitrary tax formula scripting are outside Phase 7U.
