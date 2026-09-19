# Phase 7B — Salary Structure Master

## Scope and status

Phase 7B adds a tenant-scoped Salary Structure Master that composes existing
Salary Components into reusable, effective-dated configuration. Employee salary
assignment, payroll calculation, payroll runs, and statutory tax processing are
not part of this phase.

The implementation and provider verification are complete. The SQL Server
verification blocker is **RESOLVED** following successful operator execution
against the dedicated integration database.

## Architecture

`SalaryStructure` is the tenant-owned aggregate root. A structure owns immutable
effective-dated configuration snapshots (`SalaryStructureVersion`), and each
snapshot owns `SalaryStructureComponent` rows that reference the existing
Salary Component master. The aggregate master owns the optimistic concurrency
token; changes are reconciled under that token so the same behavior works on
SQL Server and MySQL.

## Entities and calculation types

- `SalaryStructure`: tenant, code, name, description, active state, concurrency.
- `SalaryStructureVersion`: effective-from/effective-to interval and active state.
- `SalaryStructureComponent`: component reference, sequence, calculation type,
  value/formula/base component, proration/editability, bounds, and dates.
- `SalaryStructureHistory`: immutable change snapshot and serialized component
  configuration.

Supported configuration types are `FixedAmount`, `Percentage`, `Formula`, and
`Manual`. Fixed amounts require a non-negative value; percentages require a
0–100 value and a base component; formulas require formula text; manual rows do
not accept calculation inputs. Component references must belong to the current
tenant, and duplicate component rows and invalid sequences are rejected.

## Effective dating and history

Structure codes are unique within a tenant and may repeat across tenants.
Versions reject overlapping effective periods, support future-dated versions,
and preserve historical configuration for queries and audit. Removing a child
component deactivates it rather than deleting its history. Create, update,
component changes, activation/deactivation, and effective-date changes produce
immutable history records.

## Authorization and APIs

Canonical permissions are seeded for:

- `Payroll.SalaryStructure.View`
- `Payroll.SalaryStructure.Manage`
- `Payroll.SalaryStructure.ViewHistory`

The tenant-safe API is rooted at `/api/payroll/salary-structures` and supports
list/detail, create/update, activation/deactivation, history, and child
component add/update/deactivate operations. Controllers delegate validation,
tenant ownership, effective dating, history, filtering, pagination, and
concurrency to the application service.

## Frontend

Payroll navigation includes a Salary Structures page with search, status,
effective-date display, pagination, create/edit header fields, component-row
editing, conditional calculation inputs, activation, and history display. The
component selector uses the existing Salary Component API and preserves the
current application layout and authorization guards.

## Persistence and migrations

Provider-specific migrations were added without modifying the Phase 7A Salary
Component migration:

- SQL Server: `20260919055335_AddSalaryStructureMaster`
- MySQL: `20260919055441_AddSalaryStructureMaster`

Both providers create the four Salary Structure tables, tenant-scoped keys and
indexes, effective-date fields, history storage, and foreign keys to tenants,
users, salary structures, versions, and salary components.

## Verification evidence

- SQLite service tests: 4 passed, covering creation/history, tenant-scoped
  uniqueness, cross-tenant references, validation, effective overlap, child
  deactivation, and history.
- MySQL disposable-database runtime test: passed, including migration,
  create, duplicate and tenant-scoped uniqueness, paging, update,
  activation/deactivation, history, and tenant isolation.
- SQL Server focused disposable-database runtime test: passed with 1 test,
  0 failures, and 0 skips. The migration applied successfully and the test
  verified schema creation, structure/version/component/history persistence,
  create/update, component management, effective-date validation, duplicate
  code and tenant-scoped uniqueness rules, cross-tenant denial, tenant
  isolation, history, activation/deactivation, fixed amount, percentage/base
  component, and formula validation. No SQL Server provider-specific defect
  was observed.
- Frontend Salary Structure API coverage was added for list, create, update,
  and activation endpoint behavior.

The operator also confirmed the full SQL Server suite completed successfully.

## Deferred items

The following are intentionally deferred to later phases:

- Employee Salary Assignment
- Payroll Calculation Engine
- Payroll Run / payroll processing
- Statutory tax engine
