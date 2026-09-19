# Phase 7C — Employee Salary Assignment

## Status

Implementation is complete for the tenant-scoped Employee Salary Assignment
slice. The approved targeted regression strategy is green: the six-test
Employee Salary suite passed, the Payroll fast regression passed 19/19, and
SQL Server and MySQL provider verification passed. The SQL Server verification
blocker is resolved. Full backend regression is intentionally deferred to the
next major repository checkpoint because it is long-running and is not a
Phase 7C product blocker.

Payroll calculation, payroll runs, statutory processing, payslips, arrears,
retro-pay, and accounting posting are intentionally deferred.

## Architecture

`EmployeeSalaryAssignment` is the aggregate root. It references the
authoritative Employee, SalaryStructure, and exact SalaryStructureVersion
records rather than copying master data. `EmployeeSalaryComponent` stores only
employee-specific overrides permitted by the selected structure component.
The application service owns tenant checks, effective-date validation,
version resolution, overlap checks, history, pagination, and activation.

## Entities and persistence

- `EmployeeSalaryAssignment`: tenant, employee, structure/version binding,
  effective dates, CTC inputs, currency, pay frequency, change reason, status,
  and remarks.
- `EmployeeSalaryComponent`: normalized override rows linked to a structure
  component and salary component.
- `EmployeeSalaryAssignmentHistory`: immutable change snapshots including the
  assignment state and override snapshot JSON.

Provider-specific migrations were added for SQL Server and MySQL:

- `20260919064515_AddEmployeeSalaryAssignment` (SQL Server)
- `20260919064520_AddEmployeeSalaryAssignment` (MySQL)

Existing Phase 7A and 7B migrations were not modified.

## Effective dating and version binding

Assignments may be historical, current, or future-dated. Active periods for
the same tenant and employee may not overlap. Effective lookup returns the
single active assignment for a requested date and fails when no unambiguous
assignment exists.

Creation resolves and persists the exact active SalaryStructureVersion whose
effective period contains the assignment start date. The service rejects a
missing or ambiguous version and rejects inactive or cross-tenant structures.
Past assignments remain queryable and are not silently rewritten.

## Employee overrides

Only structure components marked `IsEditableAtEmployeeLevel` can receive an
override. Fixed amount, percentage, and formula overrides use the same basic
validation rules as the structure configuration. Duplicate overrides,
invalid references, cross-tenant references, and overrides for locked rows are
rejected. Formula text is stored for future calculation work; it is not
executed in this phase.

Annual/monthly CTC values are persisted as authoritative assignment inputs.
No CTC breakdown or payroll amount is derived here.

## History and authorization

Create, update, override changes, end-dating, activation, and deactivation
record immutable assignment history. The canonical permissions are:

- `Payroll.EmployeeSalary.View`
- `Payroll.EmployeeSalary.Manage`
- `Payroll.EmployeeSalary.ViewHistory`

They are registered with the existing permission seed conventions.

## APIs

The thin controller exposes tenant-safe endpoints under
`/api/payroll/employee-salary-assignments` for:

- paged list and detail retrieval;
- employee assignment list and effective-date lookup;
- create/update and activation/deactivation;
- assignment history;
- add, update, and remove employee component overrides.

## Frontend

The Payroll navigation includes Employee Salary. The page supports search,
status filtering, pagination, employee and Salary Structure selection,
effective dates, CTC inputs, change reason, remarks, history, activation, and
conditional override controls for editable structure components.

## Verification

- Focused Employee Salary suite: 6 passed, 0 failed, 0 skipped.
- Payroll fast regression (`scripts/test-payroll-fast.ps1`): 19 passed,
  0 failed, 0 skipped; build passed; duration 43 seconds.
- SQLite Employee Salary Assignment tests: 4 passed.
- MySQL Employee Salary Assignment integration: 1 passed.
- SQL Server Employee Salary Assignment integration: 1 passed, 0 failed,
  0 skipped.
- SQL Server tenant isolation and cross-tenant denial: passed; no provider
  issues remain.
- SQL Server test isolation is repeatable through unique run-specific
  identifiers and FK-safe cleanup scoped to the test-owned tenant.
- Frontend focused tests: 4 passed; full frontend suite: 577 passed.
- Backend build, TypeScript, frontend production build, lint, and
  `git diff --check` acceptance checks passed; lint retains existing warnings.
- Full backend regression: intentionally deferred to the next major repository
  checkpoint; this does not block Phase 7C closure under the approved targeted
  regression strategy.

## Deferred scope

The following are not started in Phase 7C:

- payroll calculation engine and gross/net calculation;
- Payroll Run;
- statutory tax, PF/ESI, TDS, and professional-tax engines;
- payslips and bank advice;
- arrears and retro payroll;
- accounting posting.
