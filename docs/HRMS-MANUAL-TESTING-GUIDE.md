# HRMS manual testing guide

Version: 1.0  
Purpose: End-to-end manual execution guide for a tester who has not worked on the HRMS codebase.

This guide explains how to start the application, create a tenant, configure a tenant, execute every HRMS module in dependency order, and record evidence. The detailed QA case catalogues under `qa/01-*` through `qa/17-*` contain the exhaustive endpoint-level cases; this document is the practical browser/manual runbook that tells the tester when and why to execute them.

## 1. What the tester must validate

The tester must prove that:

1. A platform administrator can create and recover a tenant safely.
2. A tenant administrator can configure the organization and security model.
3. HR can create employees and link user accounts to employee identities.
4. Employees and managers can complete self-service and approval workflows.
5. Attendance and leave data flow into payroll.
6. Payroll can be configured, calculated, approved, finalized, paid, posted, and reported.
7. Separation creates a controlled exit and final settlement.
8. One tenant cannot read or change another tenant’s data.
9. Invalid inputs, unauthorized actions, duplicate submissions, concurrency conflicts, and failed provisioning are handled safely.

## 2. Test environment and startup

### 2.1 Required software

- Windows machine with access to `D:\HRMS`.
- .NET 10 SDK: `dotnet --version` must report 10.x.
- Node.js 20.19+ or 22.12+.
- Configured database provider: MySQL, SQL Server, or the approved SQLite development fallback.
- Chromium/Chrome or Edge.
- Optional: Swagger at `http://localhost:5080/swagger` for API checks.

Do not use real employee data, production credentials, production databases, or real bank/tax information. Use disposable test data only.

### 2.2 Configure the database

Use the provider instructions in [`docs/mysql-development.md`](mysql-development.md), [`docs/sqlserver-development-and-testing.md`](sqlserver-development-and-testing.md), or the database README. The API must be able to reach the catalog database and the tenant database/shard before browser testing begins.

Confirm the selected provider and connection settings are valid. In development, startup normally applies migrations/schema initialization and seeds demo data. Do not run destructive database cleanup against a shared environment.

### 2.3 Start the API

From `D:\HRMS`:

```powershell
dotnet run --project .\Backend\HRMS.API\HRMS.API.csproj --no-launch-profile --urls http://localhost:5080
```

Confirm:

- `http://localhost:5080/health` returns `Healthy`.
- `http://localhost:5080/ready` returns `Ready`.
- Swagger opens in Development.
- The API log contains no migration or connection failure.

### 2.4 Start the frontend

In a second PowerShell window:

```powershell
cd D:\HRMS\Frontend\HRMS.Web
npm ci
npm run dev -- --host 0.0.0.0
```

Open `http://localhost:5173`.

For tenant-host routing, use the workspace URL shown by the platform screen, for example `http://demo01.localhost:5173`. If the browser cannot resolve `*.localhost`, add the required local host mapping or use the approved environment URL supplied by the environment owner.

## 3. Test identities and data

### 3.1 Development seed identities

The development seed normally includes:

| Identity | Example login | Use |
|---|---|---|
| Tenant administrator | `admin@demo01.com` | Configuration, security, full tenant administration |
| HR manager | `hr@demo01.com` | Employee and HR operations |
| Second tenant administrator | `admin@demo02.com` | Tenant-isolation testing |
| Second tenant HR manager | `hr@demo02.com` | Tenant-isolation testing |

The development password is documented in the local README/configuration. Obtain it from the environment owner rather than copying a production password into this guide.

Platform administrator credentials are created by the operator-controlled bootstrap process. Obtain the disposable platform test credentials from the environment owner.

### 3.2 Create additional test identities

For complete workflow testing, create or request these identities in the test tenant:

| Role | Why needed |
|---|---|
| Tenant Admin | Tenant setup and access control |
| HR Admin/HR Manager | Employee, leave, attendance, payroll, separation |
| Manager | Team approvals and scope checks |
| Accounts/Payroll user | Payroll, bank, accounting, statutory workflows |
| Employee | Self-service leave, attendance, payroll, loans, claims, separation |
| Second employee | Manager hierarchy and comparison tests |
| Second manager | Approval routing and scope isolation |

Create at least two departments, two designations, two employees, one manager/subordinate relationship, one salary-assigned employee, one employee with a bank account, and one employee with leave balance.

### 3.3 Naming convention

Use a unique run prefix, for example `UAT-2026-09-30-01`:

- Tenant: `UAT-20260930-01`
- Department: `UAT Engineering`
- Employee: `UAT-EMP-001`
- Email: `uat-20260930-001@example.test`
- Payroll period: `UAT Sep 2026`

This prevents collisions when the environment is shared and makes screenshots/logs searchable.

## 4. How to record a manual test

For every test, record:

| Field | Required value |
|---|---|
| Test ID | Case ID from this guide or the detailed QA catalogue |
| Date/time | Local time and timezone |
| Tester | Person executing the test |
| Environment | API URL, frontend URL, database provider, build/commit |
| User/role | Exact test identity and role, never the password |
| Preconditions | Data/configuration already present |
| Steps | Actual actions performed |
| Expected result | Result defined before execution |
| Actual result | What happened |
| Status | Pass, Fail, Blocked, Not Applicable |
| Evidence | Screenshot, downloaded file name, request/response ID, or log reference |
| Defect | Defect ID and severity if failed |

For a failed test, preserve the first failing screenshot and exact error text. Do not paste access tokens, passwords, connection strings, employee PII, or bank details into a ticket.

## 5. Phase 0 — Platform login and tenant creation

### P-01 Platform authentication

1. Open `http://platform.localhost:5173/platform/login`.
2. Sign in using the platform administrator.
3. Confirm redirect to `/platform/tenants`.
4. Refresh the page and confirm the platform session remains active.
5. Sign out and confirm protected platform pages redirect back to platform login.
6. Attempt to use a tenant user on the platform host; confirm access is denied.
7. Attempt to use a platform token on a tenant host; confirm access is denied.

Expected: platform and tenant sessions are separate; platform routes are never authorized by tenant roles.

### P-02 Create a tenant

1. Open the platform tenant list and select Create Tenant.
2. Enter a unique organization name, tenant code, workspace/host, database provider, initial administrator name, and administrator email.
3. Submit with valid data.
4. Confirm the tenant first appears as provisioning/inactive and then becomes Active only after provisioning succeeds.
5. Record the tenant host and initial administrator login.
6. Open the tenant host in a new browser context and verify that the tenant login page loads.
7. Sign in as the initial tenant administrator.

Expected: catalog tenant, tenant database/schema, initial administrator, host resolution, and active status are all created consistently.

### P-03 Tenant validation and recovery

Execute each case separately:

- Duplicate tenant code, name, or host.
- Invalid host format or reserved host.
- Unsupported database provider.
- Missing/invalid initial administrator data.
- Provisioning failure caused by unavailable database/template.
- Update inactive tenant metadata and retry.
- Retry an active tenant; confirm it is rejected.
- Deactivate and reactivate according to permission.
- Reset the development administrator password and confirm the old session/password behavior.

Expected: invalid onboarding never creates a duplicate active tenant; failed provisioning leaves a diagnosable inactive row; no database is dropped automatically.

## 6. Phase 1 — Tenant security and common configuration

Log in to the new tenant as Tenant Admin.

### S-01 Authentication and password lifecycle

1. Sign in with valid credentials.
2. Verify the dashboard and profile.
3. Refresh the browser and confirm the session restores.
4. Change the password, sign out, and sign in with the new password.
5. Test forgot-password/set-password using the configured development provider.
6. Try wrong password, unknown user, and invalid tenant/host.
7. Confirm errors do not reveal whether a user or tenant exists.

### S-02 Roles, permissions, page access, and account linking

1. Review seeded roles and permissions.
2. Create or update a test role with a small permission set.
3. Assign it to a test user.
4. Refresh that user’s session and confirm the navigation changes.
5. Directly enter a URL not granted to the user; confirm the forbidden page/403 response.
6. Remove a permission, refresh the session, and confirm access is revoked.
7. Link a user to an employee and verify employee-only pages become available.
8. Unlink the user and verify self-service pages are blocked again.

Expected: hidden navigation is only cosmetic; backend authorization remains the final security boundary.

### S-03 Tenant configuration

Configure and verify:

- Tenant branding/logo/name/primary color.
- Login identifier mode.
- Password recovery channel/provider.
- Email/SMS development provider behavior.
- Page access rules.
- Role assignment and scope rules.

## 7. Phase 2 — Masters and employee-code configuration

### M-01 Master data

Open Masters and configure in this order:

1. Holding company/organization.
2. Countries, states, and cities.
3. Work locations and addresses.
4. Functions, sub-functions, departments, sub-departments, sections, and subsections.
5. Grades, designations, positions, employee types, cost centers, banks, and other lookup data.

For each master: create, list/search, edit, deactivate if supported, duplicate a code/name, and attempt deletion while referenced by an employee.

Expected: uniqueness is tenant-scoped; referenced master records cannot be removed in a way that breaks employee history.

### M-02 Employee-code rules

1. Open Employee Code Configuration.
2. Create a rule with prefix/segments and a sequence scope.
3. Configure reset period and starting number.
4. Save and activate the rule.
5. Create two employees and confirm codes are generated uniquely.
6. Test duplicate/rule conflict, inactive rule, sequence increment, and period reset behavior.
7. Review rule version/history.

## 8. Phase 3 — Employee management

### E-01 Create an employee

1. Open Employees and select New Employee.
2. Enter personal details.
3. Select department, designation, employee type, work location, joining date, and status.
4. Add supervisor/reporting manager.
5. Add contact, address, family, education, previous employment, bank, documents, and additional information.
6. Save the employee.
7. Confirm the employee appears in search/list/export and the detail page shows all saved sections.

### E-02 Employee validation and hierarchy

Test:

- Duplicate employee code/email.
- Invalid dates, leaving date before joining date, missing required fields.
- Self as manager.
- Manager cycle through direct or indirect reporting.
- Cross-tenant department/designation/manager references.
- Delete employee with direct reports or dependent records.
- Status changes to resigned/terminated with required leaving date.

Expected: field-level validation is clear, tenant boundaries cannot be bypassed, and history-bearing employees are not destructively deleted.

### E-03 Link identity and verify self-service

1. Create/link a user account to the employee.
2. Sign in as the employee in a separate browser context.
3. Verify My Profile, My Leave, My Attendance, My Payslips, and other permitted self-service pages.
4. Verify the employee cannot access another employee’s records by changing an ID in the URL/API.

## 9. Phase 4 — Attendance

### A-01 Configure attendance

1. Create shifts and shift patterns.
2. Configure applicability rules.
3. Assign a roster to employees.
4. Upload a valid roster file and verify imported rows.
5. Upload invalid, duplicate, cross-tenant, and malformed rows.
6. Register an attendance device.
7. Configure employee/device mapping.
8. Run device synchronization and inspect sync history/issues.

### A-02 Employee attendance and requests

1. Ingest or create test punches.
2. Verify daily attendance derivation, late/early/missing-punch exceptions, and calendar/holiday behavior.
3. As employee, submit regularization and on-duty requests.
4. As manager, approve one and reject one with comments.
5. Submit overtime and comp-off; approve/finalize with the appropriate manager/HR user.
6. Confirm the employee sees status and history changes.

### A-03 Monthly finalization

1. Create an attendance period.
2. Process it and review exceptions.
3. Resolve/correct an exception.
4. Reprocess and confirm totals.
5. Close/finalize the period.
6. Attempt edits after close; confirm they are blocked.
7. Reopen using the explicit permission and record the audit/history event.
8. Confirm payroll can read the finalized attendance snapshot.

## 10. Phase 5 — Leave management

### L-01 Configure leave

1. Create leave types.
2. Create the leave period.
3. Configure working-day calendar, holidays, weekends, and partial-day rules.
4. Create a policy with eligibility, entitlement, accrual, clubbing, attachment, cancellation, and approval rules.
5. Publish the policy.
6. Import or allocate opening balances.
7. Verify accrual/reminder worker behavior in the environment logs or UI history.

### L-02 Employee request and approval

1. Sign in as an employee linked to the test employee.
2. Open Apply Leave and select a valid date range/type.
3. Confirm working days, holidays, balance, and overlap warnings.
4. Submit the request.
5. Confirm it appears in My Leave Requests and the manager inbox.
6. Approve one request and reject another.
7. Test withdraw, cancel, attachment requirement, insufficient balance, overlap, past/closed period, and unauthorized employee access.
8. Verify balance reservation/consumption/reversal and audit history.

### L-03 Leave reporting

Verify dashboard totals, team calendar, balances, approval history, imports, and exports match the underlying requests and balances.

## 11. Phase 6 — Payroll foundation

Payroll must use employees, finalized attendance, leave, bank details, and approved inputs created in earlier phases.

### PR-01 Salary and statutory configuration

1. Create salary components: earnings, deductions, employer contributions, taxable/non-taxable flags.
2. Create salary structures and versions.
3. Assign a structure/salary to an employee with an effective date.
4. Configure statutory rules and effective-dated versions.
5. Configure payroll accounting/GL mappings and cost centers.
6. Configure loan products, reimbursement categories, variable-pay plans, and payroll input templates as applicable.
7. Review each configuration history/version.

### PR-02 Payroll period and inputs

1. Create/open a payroll period.
2. Import or enter payroll inputs.
3. Add approved overtime, leave effects, reimbursements, loans, variable pay, adjustments, retro/arrears, and statutory inputs.
4. Validate inputs.
5. Submit them through the configured approval flow.
6. Test duplicate import, invalid employee, invalid amount, closed period, unauthorized input, and self-approval prevention.

### PR-03 Calculate, approve, finalize

1. Create a payroll run for the period.
2. Prepare and calculate.
3. Review employee results, earnings, deductions, net pay, statutory values, attendance/leave effects, errors, and controls.
4. Correct source data or use an approved adjustment/recalculation route.
5. Recalculate and confirm the audit trail.
6. Submit for approval.
7. Approve with a different authorized user.
8. Finalize the run.
9. Attempt modification after finalization; confirm it is blocked.

### PR-04 Payroll outputs

Verify:

- Payslip generation and publication.
- Employee My Payslips access and tenant/employee isolation.
- Bank advice generation, validation, approval, export, cancellation, and regeneration.
- Accounting journal generation, debit/credit balancing, validation, approval, posting, export, and duplicate prevention.
- Statutory compliance calculation, return generation, approval, export, and filed status.
- Payroll analytics, reconciliation, variance, anomaly, exception, and report exports.

## 12. Phase 7 — Payroll employee self-service and related modules

### X-01 Loans and advances

1. Configure a loan/advance product and eligibility rules.
2. Employee submits a request.
3. Manager/HR approves or rejects.
4. Disburse and verify EMI schedule.
5. Verify payroll deduction, manual repayment, partial prepayment, early closure, cancellation, and final-settlement integration.

### X-02 Reimbursements and claims

1. Configure reimbursement categories/limits.
2. Employee submits a claim with attachment.
3. Manager/HR approves or rejects.
4. Settle the claim and verify payroll/accounting effect.
5. Test duplicate, over-limit, invalid receipt, cancellation, and unauthorized access.

### X-03 Bonus and variable pay

1. Configure and publish a variable-pay plan/version.
2. Calculate eligible awards.
3. Create/override an award where permitted.
4. Submit, approve, reject, cancel, and settle through payroll/final settlement.

### X-04 Tax declarations and year-end tax

1. Configure declaration cycle, categories, items, cut-off, and proof rules.
2. Employee submits declarations and proofs.
3. Reviewer approves/rejects/reopens.
4. Lock the cycle and verify approved-input resolution.
5. Create, calculate, submit, approve, close, and export a year-end tax run.

### X-05 Payroll adjustments and off-cycle

Create an adjustment/off-cycle run, validate approval separation, process it, verify result/payslip/accounting effects, and test cancel/reverse behavior.

## 13. Phase 8 — Separation and final settlement

1. As an employee, submit a resignation.
2. As manager, review/approve/reject.
3. As HR, set or revise notice period and last working date.
4. Start clearance tasks for HR, IT, Finance, functional owners, and assets.
5. Complete, waive, reopen, and reject clearance tasks as allowed.
6. Complete exit interview configuration, employee survey, HR review, and feedback.
7. Generate/download separation documents.
8. Calculate separation benefits, gratuity, leave encashment, notice recovery, loans, reimbursements, and other settlement values.
9. Review and approve final settlement.
10. Generate final payroll/accounting outputs.
11. Close the separation and verify employee status, access, history, and final-settlement audit.

Negative tests: resignation withdrawal, invalid LWD revision, missing clearance, unauthorized waiver, incomplete documents, duplicate settlement, settlement after closure, and cross-tenant separation ID access.

## 14. Phase 9 — Tenant isolation and security regression

Run this after every major module and again at the end.

1. Create data in Tenant A and confirm it is visible only in Tenant A.
2. Sign in to Tenant B and search by Tenant A codes, employee IDs, request IDs, payroll run IDs, and document IDs.
3. Attempt direct URL/API access using Tenant A identifiers from Tenant B.
4. Attempt to use a tenant token on platform routes and a platform token on tenant routes.
5. Remove a permission and refresh the session; confirm both menu and API access are denied.
6. Test manager scope: manager A must not approve or view employees outside the permitted reporting scope.
7. Test employee scope: employee A must not view employee B’s self-service records.
8. Verify exports and reports apply the same tenant and scope restrictions as on-screen lists.

Expected: no cross-tenant data, token substitution, IDOR, permission bypass, or stale-permission access.

## 15. Detailed QA catalogues

Use these for exhaustive case-by-case execution after completing the guided run:

| Module | Detailed catalogue |
|---|---|
| Authentication | [`qa/01-authentication`](../qa/01-authentication/README.md) |
| Platform/tenant onboarding | [`qa/02-platform-admin`](../qa/02-platform-admin/README.md) |
| Masters | [`qa/03-masters`](../qa/03-masters/README.md) |
| Employee code | [`qa/04-employee-code`](../qa/04-employee-code/README.md) |
| Employees | [`qa/05-employee`](../qa/05-employee/README.md) |
| RBAC/account linking | [`qa/06-rbac-account-linking`](../qa/06-rbac-account-linking/README.md) |
| Leave | [`qa/07-leave`](../qa/07-leave/README.md) |
| Attendance | [`qa/08-attendance`](../qa/08-attendance/README.md) |
| Separation | [`qa/09-separation`](../qa/09-separation/README.md) |
| Payroll | [`qa/10-payroll`](../qa/10-payroll/README.md) |
| Loans and advances | [`qa/11-loans-advances`](../qa/11-loans-advances/README.md) |
| Bonus/variable pay | [`qa/12-bonus-variable-pay`](../qa/12-bonus-variable-pay/README.md) |
| Statutory compliance | [`qa/13-statutory-compliance`](../qa/13-statutory-compliance/README.md) |
| Tax/year-end tax | [`qa/14-tax-declarations-year-end-tax`](../qa/14-tax-declarations-year-end-tax/README.md) |
| Bank advice/disbursement | [`qa/15-bank-advice-salary-disbursement`](../qa/15-bank-advice-salary-disbursement/README.md) |
| GL/accounting | [`qa/16-gl-accounting-integration`](../qa/16-gl-accounting-integration/README.md) |
| Analytics/reconciliation | [`qa/17-payroll-analytics-reconciliation`](../qa/17-payroll-analytics-reconciliation/README.md) |

The generated workbook `qa/HRMS_Test_Cases.xlsx` is useful for assigning cases and capturing Pass/Fail, but the tester should read the relevant module README and this guide first.

## 16. Final end-to-end acceptance scenario

Execute this scenario in one disposable tenant:

1. Platform admin creates and activates a tenant.
2. Tenant Admin configures roles, masters, employee-code rule, leave policy, shifts, salary structure, statutory rules, and payroll accounting.
3. HR creates a manager and employee, links both accounts, and assigns roles.
4. Device/manual punches create attendance; employee submits one regularization request and one leave request.
5. Manager approves the regularization and leave request and rejects a second request.
6. HR finalizes attendance and verifies leave balance changes.
7. Payroll assigns salary, creates a period/run, imports approved inputs, calculates, approves, finalizes, publishes payslip, creates bank advice, posts accounting, and generates statutory output.
8. Employee verifies payslip, leave history, attendance, and any approved self-service transaction.
9. Employee submits resignation; manager and HR complete approval, notice, clearance, exit interview, documents, benefits, final settlement, and closure.
10. Tenant B attempts to access every Tenant A object created in this scenario and is denied.

Acceptance is complete only when the happy path, negative paths, audit/history, exports, role restrictions, tenant isolation, and recovery behavior have all been recorded.

## 17. Sign-off template

```text
Test cycle:
Environment/build:
Database provider:
Tenant:
Tester:
Execution dates:

Platform/tenant onboarding:       PASS / FAIL / BLOCKED
Authentication/security:          PASS / FAIL / BLOCKED
Masters/employee code:            PASS / FAIL / BLOCKED
Employee management:              PASS / FAIL / BLOCKED
Attendance:                       PASS / FAIL / BLOCKED
Leave:                             PASS / FAIL / BLOCKED
Payroll foundation/run:           PASS / FAIL / BLOCKED
Payroll outputs/accounting:       PASS / FAIL / BLOCKED
Loans/reimbursements/variable:   PASS / FAIL / BLOCKED
Tax/statutory:                    PASS / FAIL / BLOCKED
Separation/final settlement:      PASS / FAIL / BLOCKED
Tenant isolation/security:        PASS / FAIL / BLOCKED

Open defects:
Known limitations:
Evidence location:
Tester signature/date:
Business owner signature/date:
```
