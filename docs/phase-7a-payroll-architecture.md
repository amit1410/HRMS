# Phase 7A Payroll Foundation

## Scope

Phase 7A introduces only the tenant-scoped Salary Component Master. It does not
create Pay Groups, Salary Structures, employee salary assignments, payroll runs,
payslips, statutory calculators, journals, bank files, arrears, or
reimbursements.

## Planned domain

```text
Payroll
├── Salary Component Master       ← Phase 7A
├── Pay Group                     ← later
├── Payroll Calendar              ← later
├── Salary Structure              ← later
├── Employee Salary Assignment    ← later
├── Payroll Inputs                ← later
├── Payroll Run                   ← later
├── Payroll Calculation           ← later
├── Statutory Calculation         ← later
├── Payroll Finalization          ← later
├── Payslip                       ← later
└── Payroll Reports/Exports       ← later
```

`SalaryComponent` is a reusable definition. It contains no employee amount or
currency. Amounts and percentages belong to future Salary Structure and
Employee Salary Assignment records. This keeps master, setup, period input,
and immutable transaction concerns separate.

## Salary Component design

The component is a tenant-scoped, directly effective-dated master record with
an immutable `SalaryComponentHistory` audit stream. This follows the existing
HRMS effective-date and history conventions without introducing a second
versioning subsystem in the first Payroll milestone. Updates increment the
provider-neutral concurrency token and append a typed history snapshot.

Supported classifications are:

- Earning, Deduction, EmployerContribution, Reimbursement, Information
- FixedAmount, Percentage, Formula, ManualInput, AttendanceBased, LeaveBased,
  Statutory
- statutory metadata for Provident Fund, ESI, Professional Tax, Labour Welfare
  Fund, Income Tax, Gratuity, and other future rules

Formula is a classification only. Phase 7A does not evaluate user-provided
expressions or executable code.

`Code` is trimmed, normalized to uppercase, and unique per tenant by database
constraint and application validation. `Name` is a display label, not a
foreign/business key. Components are retired by deactivation rather than
deleted.

Statutory consistency and basic gross/net semantics are enforced in the
Application service. Employer contributions cannot affect employee net pay.
The actual calculation and rounding engine is deferred; future monetary values
must use the repository's decimal strategy, never floating-point types.

## Authorization and Page Access

The existing permission pipeline is used:

```text
authentication → Payroll permission → tenant context → Salary Component query
```

Permissions:

- `Payroll.SalaryComponent.View`
- `Payroll.SalaryComponent.Manage`
- `Payroll.SalaryComponent.ViewHistory`

TenantAdmin receives the canonical all-permissions grant. HRAdmin and SuperHR
receive the Salary Component permissions through existing seed conventions.
Accounts, IT, Employee, Manager, and HRBP are not implicitly granted Payroll
management. Page visibility is configured through the existing Page Access
system and never substitutes for API permission checks.

## Future integration boundaries

- Employment: future Payroll eligibility and applicability resolve effective
  employment as of the payroll period; component definitions do not copy
  department, grade, location, or other employment attributes.
- Attendance: finalized/approved Attendance outputs become future payroll
  inputs for overtime, late deductions, and incentives. Payroll does not
  recalculate raw punches.
- Leave: authoritative approved Leave and unpaid Leave/LWP decisions become
  future period inputs. Payroll does not duplicate Leave policy calculation.
- Statutory: component metadata identifies future statutory ownership; separate
  rules/calculators will implement PF, ESI, Professional Tax, TDS, and other
  jurisdictional behavior later.

Future pipeline:

```text
Payroll period → effective employment → Pay Group → Salary Structure
→ employee assignment → fixed components → Attendance/Leave/manual inputs
→ statutory rules → gross → deductions → employer contributions → net
→ validation → finalization → immutable result
```

Currency, rounding, and statutory policy should be explicit future payroll
configuration. Salary Component itself remains currency-neutral and amount-free.

## API and UI

Tenant-scoped endpoints are exposed under `/api/payroll/salary-components` for
list, detail, create, update, activate/deactivate, and history. List filtering,
sorting, counting, and paging are server-side with the shared page-size limit.
The frontend route is `/payroll/salary-components` and is protected by the
existing permission/page-visibility infrastructure.

## Provider and migration strategy

Salary Components belong in the tenant database. SQL Server and MySQL receive
provider-specific EF migrations generated from the same provider-neutral model.
Tenant composite relationships, restrictive deletes, concurrency, and
tenant-scoped indexes are preserved. No catalog tables or destructive database
initialization are involved.

## Deferred Phase 7 work

Pay Group, Payroll Calendar, Salary Structure, Employee Salary Assignment,
Payroll Inputs, Payroll Processing, statutory calculations, payslips, reports,
exports, rounding policy, currency policy, and formula evaluation remain later
milestones.

## Phase 7A closure

Status: **COMPLETE**

Final acceptance was completed against both supported tenant database
providers.

- SQL Server dedicated Salary Component runtime verification: PASS.
- SQL Server migration `20260919033410_AddSalaryComponentMaster`: PASS.
- SQL Server `SalaryComponents` and `SalaryComponentHistories` tables: PASS.
- SQL Server create, duplicate-code validation, tenant-scoped uniqueness,
  same-code-across-tenants, filtering, pagination, update,
  activation/deactivation, history, validation, cross-tenant denial, and
  tenant isolation: PASS.
- SQL Server provider-specific issues: none.
- MySQL migration, runtime verification, and tenant isolation: PASS.
- Backend suite: 1,264 passed, 0 failed, 66 environment-skipped.
- Focused Salary Component frontend tests: 2 passed.
- Full frontend suite: 575 passed, 0 failed.
- Backend, TypeScript, and frontend production builds: PASS.
- Lint: PASS with existing warnings.
- `git diff --check`: PASS.

The SQL Server verification blocker is **RESOLVED**. Phase 7B and all other
Payroll milestones remain deferred.

PHASE 7A SALARY COMPONENT MASTER: COMPLETE
