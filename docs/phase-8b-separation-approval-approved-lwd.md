# Phase 8B — Separation Approval & Approved Last Working Date

Phase 8B extends the Phase 8A separation foundation with a narrow, server-enforced approval workflow. It does not add clearance, exit interviews, settlement orchestration, gratuity, leave encashment, or employment termination.

## Reused capabilities

The implementation reuses `EmployeeSeparation`, `EmployeeSeparationEvent`, `SeparationReason`, `EmployeeManagerResolver`, the authenticated account-to-employee link, tenant context, existing permission policies, `EmployeeEmployment` notice fields, and the existing Leave notice-period resolver. Payroll Final Settlement, gratuity, separation benefits, PayrollResult, bank advice, and GL services are not called.

## Lifecycle and authorization

Draft requests submit to `Submitted`. Employee-initiated cases are reviewed by the effective manager and then enter `HrReview`; employer-initiated cases use the same controlled review path. Manager approve/reject, HR approve/reject, withdrawal, and LWD revision are server-authorized and append immutable events. Manager resolution is evaluated for the separation request date. The employee cannot review their own case, and the initiating HR maker cannot perform final approval or rejection.

HR approval is the only point at which `ApprovedLastWorkingDate` becomes authoritative. Revisions are allowed only while the case is in `HrReview`; the prior proposed date remains in event history and the revised value is not silently overwritten in history. Post-approval LWD mutation is rejected and deferred to a later controlled phase.

## Notice-period and employment boundary

For employee resignations, the notice start is the stored request date. Approval sets the separation notice end to the approved LWD and synchronizes the existing effective `EmployeeEmployment` record to `NoticeStatus = Active`, `NoticeStartDate`, and `NoticeEndDate`. Notice served/shortfall days use an inclusive day-count convention; monetary recovery is not calculated. Approval does not set `Employee.DateOfLeaving`, deactivate the employee, or close employment. Leave continues to consume its existing employment notice contract without a new Leave rule.

The separation workflow and notice synchronization are saved in one transaction. A concurrency or persistence failure leaves the separation unapproved and employment notice fields unchanged.

## Deferred scope

Clearance/no-dues, asset return, exit interview, final settlement orchestration, gratuity, leave encashment, notice recovery, relieving/experience letters, account disablement, rehire, and alumni access remain future work. Payroll remains authoritative for all monetary calculations.

## Verification

The HR approval transaction is atomic. A deterministic EF save interceptor failure before commit leaves the separation unapproved, leaves employment notice fields unchanged, persists no approval or notice-activation event, and allows a fresh-context retry to succeed exactly once. This is a test-only seam; no public failure endpoint, environment chaos switch, or production debug hook exists.

The dedicated `SeparationApprovalConcurrencyTests` suite has six independently discoverable scenarios: manager approve versus employee withdrawal, manager approve versus manager rejection, HR approve versus HR rejection, HR approve versus LWD revision, HR approve versus employee withdrawal, and two HR approvals. The suite passes 6/6 and asserts one authoritative lifecycle outcome with no partial notice state or duplicate approval events.

Focused Phase 8B evidence covers manager review, HR approval/rejection, approved LWD and revision, effective-date authorization, self-approval prevention, duplicate-active protection, notice synchronization, Leave notice visibility, invalid transitions, concurrency, tenant isolation, and absence of Payroll side effects. Phase 8A tests remain a regression gate. SQL Server acceptance is prepared through the shared provider wrapper; actual results are reported only when the operator connection is available. MySQL acceptance uses the shared provider flow and both migration snapshots remain synchronized when no Phase 8B schema change is required.
