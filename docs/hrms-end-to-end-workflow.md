# HRMS end-to-end module workflow

This document is the current functional workflow reconstructed from the React route table, API controllers, application services, domain entities, authorization model, and the existing platform/phase documentation.

## 1. Platform entry and tenant lifecycle

Platform administration is a separate security domain from tenant administration.

```text
platform.localhost
  -> Platform login
  -> Platform tenant list/detail
  -> Create or repair tenant
  -> Provision catalog row + tenant database
  -> Create initial tenant administrator
  -> Activate tenant

<workspace>.localhost
  -> Workspace/tenant resolution
  -> Tenant login
  -> Tenant dashboard
```

### Platform administrator workflow

1. Open the platform host and sign in through `/platform/login`.
2. The platform token is validated against catalog platform identity tables. A tenant token or tenant `SuperAdmin` role cannot authorize platform routes.
3. Open `/platform/tenants` and review tenant status, provider, host, and provisioning state.
4. Create a tenant with organization identity, host/workspace, database provider, and initial administrator details.
5. The platform service creates the catalog tenant as `Inactive`, provisions the selected tenant store, creates the initial tenant administrator, and activates the tenant only after successful completion.
6. If provisioning fails, retain the inactive row, correct metadata if required, and retry the existing tenant. Do not create a duplicate tenant or drop a partially provisioned database.
7. In development, the temporary administrator password may be shown once. In non-development environments, onboarding should use an invitation/password setup provider before being opened for production use.

Relevant UI/API: `PlatformLoginPage`, `PlatformTenantsPage`, `PlatformAuthController`, `PlatformTenantsController`, `PlatformTenantService`, `TenantProvisioningService`.

## 2. Tenant login and common shell

1. The customer host is resolved to a tenant before tenant database access.
2. The user signs in through `/login`; authentication is tenant-scoped and returns access/refresh tokens, roles, permissions, and optional employee identity.
3. The frontend restores the session with `/api/auth/refresh` or `/api/auth/me`.
4. The default signed-in landing page is `/dashboard`.
5. The shell loads tenant branding, filters navigation by permission, and still protects every route and API endpoint server-side.
6. A user without a linked employee identity can use administrative features but cannot use self-service screens marked `RequireEmployeeIdentity`.

Common recovery paths are `/forgot-password`, `/set-password`, `/change-password`, and tenant login/password-recovery configuration pages.

## 3. First-time tenant setup: security and reference data

Complete this before employee creation.

1. Configure tenant login identifiers and password recovery.
2. Configure roles, permissions, role assignments, scopes, and page access.
3. Link user accounts to employee records when the employee identity exists.
4. Load master data: holding company, organization, locations, geography, departments, designations, functions, sub-departments, sections, subsections, grades, cost centers, banks, employee types, work locations, and related lookup values.
5. Configure employee-code rules, segments, sequence scope, reset period, and approval/change history.

The important control is ordering: employee-code configuration and master data must be usable before employee hiring; account-to-employee linking must happen before self-service payroll, leave, attendance, or separation actions.

## 4. Employee lifecycle

```text
Employee master data
  -> employment and supervisor hierarchy
  -> contact/address/family/education/documents/bank details
  -> account-to-employee link
  -> active employee
  -> changes/history
  -> resignation/termination
```

1. HR opens `/employees` and creates an employee using the configured employee-code rule.
2. Set personal, contact, address, family, education, previous employment, bank, employment, department, designation, and reporting-manager data.
3. Validate tenant-safe references, unique employee code/email, joining/leaving dates, status, and reporting-manager cycles.
4. Link the employee to a user account through Account–Employee Links.
5. Assign roles and page access according to the employee’s responsibilities.
6. Maintain changes through employee detail and history rather than deleting records that have downstream attendance, payroll, or leave references.
7. Use employee export/import only with the relevant permission and tenant filters.

The employee record is the shared identity used by leave, attendance, payroll, reimbursements, loans, and separation.

## 5. Attendance workflow

### Setup

1. Configure shifts and shift patterns.
2. Configure applicability and roster assignments, including bulk roster upload where required.
3. Register attendance devices, configure mappings, and validate device health/connectivity.
4. Run or schedule device synchronization and resolve imported punch issues.

### Daily employee and manager operations

1. Device punches or approved manual inputs enter attendance processing.
2. Employees review `/attendance/my-attendance` and `/attendance/my-exceptions`.
3. Employees submit regularization and on-duty requests through `/attendance/requests`.
4. Managers review team attendance and approve/reject requests.
5. Employees submit overtime and comp-off requests; managers/HR review and finalize them.
6. HR uses Attendance Operations for exceptions, administrative corrections, reconciliation, and history.

### Monthly closure and payroll handoff

1. Create the attendance period.
2. Process daily/monthly attendance and resolve exceptions.
3. Review the monthly view and payroll snapshot.
4. Close/finalize the period; reopen only with the explicit reopen permission.
5. Payroll consumes the finalized attendance snapshot for salary calculation.

Relevant UI includes the attendance foundation, shift patterns, personal/team views, requests, operations, devices, reports, monthly finalization, overtime, and comp-off pages. Relevant backend areas are `AttendanceFoundationController`, `AttendanceWorkflowController`, `AttendanceDevicesController`, `AttendanceOperationsController`, `AttendanceMonthlyController`, `AttendanceReportController`, `OvertimeController`, and `CompOffController`.

## 6. Leave management workflow

### Configuration

1. Create leave types.
2. Create leave periods and working-day calendars.
3. Configure leave policies: eligibility, entitlement/accrual, request rules, clubbing, cancellation, attachment, and policy calendar.
4. Publish the policy and import/open balances where legacy balances are required.
5. Configure accrual and approval reminders.

### Employee request lifecycle

```text
Eligible balance
  -> draft/preview
  -> submitted
  -> manager/HR approval
  -> approved and balance reserved/allocated
  -> taken or cancelled/withdrawn
  -> period close/accrual history
```

1. Employee opens Apply Leave, selects dates/type, and reviews working-day and balance calculations.
2. Submit the request; the backend applies tenant, employee, overlap, balance, policy, and concurrency checks.
3. Employee tracks the request in My Leave Requests and may withdraw/cancel only when the state and permission allow it.
4. Manager/HR reviews the approval inbox and approves or rejects with comments.
5. HR monitors balances, imports corrections, team calendar, dashboard, and reports.
6. Period close and background accrual processes produce auditable balance history.

## 7. Payroll workflow

Payroll should start only after organization data, employee employment data, attendance finalization, and leave balances are reliable.

```text
Payroll foundation
  -> salary components
  -> salary structures
  -> employee salary assignment
  -> statutory configuration
  -> payroll period
  -> payroll inputs and approvals
  -> calculation and controls
  -> review/approve/finalize
  -> payslip/bank/accounting/statutory outputs
```

1. Define salary components and earning/deduction rules.
2. Build salary structures and assign them to employees with effective dates.
3. Configure statutory rules, tax declarations, investment proofs, and payroll accounting mappings.
4. Create and open the payroll period.
5. Import or enter payroll inputs: attendance, leave, overtime, reimbursements, loans, variable pay, adjustments, retro/arrears, and other approved inputs.
6. Validate and submit inputs through their individual approval workflows.
7. Prepare and calculate the payroll run.
8. Review controls, exceptions, analytics, reconciliation, and calculation results.
9. Recalculate or correct only through controlled adjustment/off-cycle/retro workflows.
10. Approve and finalize the run; then generate/publish payslips.
11. Generate, validate, approve, and export bank advice.
12. Generate, validate, approve, post, and export accounting entries.
13. Generate statutory compliance returns and statutory filings, submit them through the configured external process, and record filing status.
14. Run year-end tax processing, employee declarations, previous-employer inputs, approvals, closure, and tax output exports.

Employee self-service payroll starts after employee linking: payslips, loans, reimbursements, separation benefits, variable pay, payroll adjustments, and tax declarations.

## 8. Separation and final settlement workflow

```text
Employee resignation
  -> manager review
  -> HR review
  -> notice/LWD decision
  -> clearance tasks
  -> exit interview and documents
  -> benefits/final settlement
  -> approval
  -> closure and payroll handoff
```

1. Employee submits a resignation through My Separation, or HR initiates a separation where policy permits.
2. Manager reviews; HR reviews and accepts/rejects/revises the last working date according to permissions.
3. Apply notice-period rules, waiver, shortfall, and approved LWD controls.
4. Start clearance: HR, functional departments, assets, finance, IT, and other configured task owners complete or waive tasks.
5. Complete exit interview configuration, employee survey, HR review, and feedback.
6. Generate and manage separation documents and templates.
7. Calculate separation benefits, gratuity, leave encashment, notice recovery, loans, reimbursements, and other settlement components.
8. Review and approve the final settlement.
9. Generate final payroll/settlement outputs, close the separation, and update employee status without deleting historical records.

Relevant UI/API areas include separation inboxes, clearance operations, exit interviews, documents, settlement dashboard, benefits, final settlement, and exit closure.

## 9. Reporting, controls, and audit

Use reports after operational workflows, not as a substitute for finalization.

- Dashboard: headcount, department distribution, recent hires, and tenant-level operational summary.
- Leave: balances, requests, approvals, calendar, and leave reports.
- Attendance: daily/monthly/exception reports, device history, operations, and monthly close.
- Payroll: analytics, reports, controls/configuration health, production health, reconciliation, registers, payslips, bank advice, accounting, statutory outputs, and audit/history screens.
- Security: role assignment history, page access, account-employee link history, and configuration histories.
- Separation: clearance status, documents, exit interview, benefits, settlement, and closure history.

## 10. Review findings and implementation risks

### Strong foundations observed

- Platform and tenant identity are separated, with platform routing before tenant shard resolution.
- Tenant claims, permission policies, fallback authorization, tenant-aware relationships, and provider-aware sharding are treated as security boundaries.
- The frontend route guards mirror backend permissions and employee-identity requirements.
- Attendance, leave, payroll, and separation contain explicit state-transition services rather than only CRUD endpoints.
- There is substantial automated coverage in frontend tests, backend tests, and QA traceability packs.

### Items to close before calling the workflow production-complete

1. **Navigation exposure is incomplete compared with the route table.** Several implemented payroll and separation routes are reachable by URL but are not obvious in `src/layout/navigation.ts` (for example statutory configuration, loans, reimbursements, variable pay, adjustments, inputs, year-end tax, statutory filings, analytics, reports, off-cycle, exit interview, clearance, settlement, documents, and employee self-service variants). Decide whether these are intentionally embedded secondary screens or should receive explicit menu entries.
2. **Attendance menu permissions are broad.** Several screens use `Attendance.View` at the route/menu level while the backend has more specific permissions such as regularization, on-duty, roster, and device permissions. Confirm that the page-level controls fully enforce the narrower actions and that users do not see misleading empty screens.
3. **Platform onboarding is operationally incomplete outside development.** Invitation/password setup and persistent platform audit storage are identified in the existing documentation as deployment work.
4. **Workflow acceptance should test cross-module handoffs.** Add or retain scenarios that prove finalized attendance reaches payroll, approved leave affects balances/payroll, separation updates final settlement, and employee unlinking/status changes block self-service without exposing another tenant’s data.
5. **Use the dependency order in this document for UAT.** A module should not be signed off only on isolated CRUD tests; each module needs happy path, rejection, approval, concurrency, tenant-isolation, history, and recovery coverage.

## 11. Recommended UAT execution order

1. Platform login and tenant provisioning/retry.
2. Tenant login, password lifecycle, roles, page access, and account-employee link.
3. Masters and employee-code configuration.
4. Employee creation, hierarchy, profile, bank, and employment history.
5. Attendance setup, device sync, requests, exceptions, monthly finalization.
6. Leave setup, balances/accrual, request, approval, cancellation, and period close.
7. Payroll setup, salary assignment, inputs, run, approval, finalization, payslip, bank, accounting, and statutory flows.
8. Employee self-service validation for payslip, leave, attendance, loans, reimbursements, tax, variable pay, and adjustments.
9. Separation, clearance, exit interview, documents, benefits, final settlement, and closure.
10. Cross-tenant security, audit/history, exports, failure recovery, and production-readiness checks.
