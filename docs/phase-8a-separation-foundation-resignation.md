# Phase 8A — Separation Foundation & Resignation

## Scope

Phase 8A introduces tenant-scoped separation reason masters, employee separation cases, employee resignation, HR-initiated separation, lifecycle transitions, and append-only lifecycle history. Clearance, exit interviews, approved-LWD orchestration, and payroll settlement are intentionally deferred.

## Reused capabilities

- `EmployeeEmployment` notice-period fields and `NoticeStatus` remain the existing employment compatibility contract.
- `EffectiveEmploymentResolver` and `EmployeeManagerResolver` remain authoritative for effective employment and manager scope.
- `EmployeeIdentityResolver` resolves self-service identity only through the authenticated account-to-employee link.
- Existing tenant filters, `Result<T>`/ProblemDetails mapping, permission attributes, audit conventions, final settlement, gratuity, and separation-benefit services are reused; Phase 8A does not call or duplicate the monetary engines.

## Ownership and boundaries

`EmployeeSeparation` owns the separation request lifecycle and requested LWD. Draft and submitted requests do not mutate employment, `DateOfLeaving`, leave balances, payroll results, final settlement, gratuity, or separation benefits. A future approved-separation workflow may synchronize approved notice values to the existing effective employment fields and then orchestrate canonical Payroll final settlement.

Leave continues to use its current notice-period runtime contract. Phase 8A does not change Leave rules.

## Lifecycle and history

The server-enforced Phase 8A states are Draft, Submitted, ManagerReview, HrReview, Approved, Rejected, Withdrawn, NoticePeriod, ReadyForExit, Exited, and Cancelled. Phase 8A currently exposes Draft → Submitted and Draft/Submitted → Withdrawn for employee requests; future review and exit transitions are reserved for later phases. Every mutation appends an `EmployeeSeparationEvent`; there is no update/delete history API.

`ActiveEmployeeKey` plus tenant-scoped active-case validation prevents more than one active case for an employee. The SQL Server model uses a filtered unique index; the MySQL model uses provider-native nullable unique-index semantics.

## Authorization and isolation

Permissions are scoped as `Separation.ViewSelf`, `ViewTeam`, `ViewAll`, `CreateSelf`, `Initiate`, `Submit`, `Withdraw`, `Review`, `Manage`, and `ViewHistory`. Self-service never accepts an arbitrary employee ID. HR initiation, team visibility, and all reads are tenant scoped. Manager visibility resolves effective manager relationships through `EmployeeManagerResolver`.

## Provider parity

`SeparationFoundationProviderAcceptance.RunAsync` is shared by MySQL and SQL Server wrappers. New provider migrations are `AddSeparationFoundationPhase8A`; historical Phase 7A–7Y migrations are not changed. Provider acceptance must be run when the corresponding operator database is available.

## Deferred scope

Multi-level approvals, approved-LWD synchronization, notice waiver/recovery, clearance, no-dues, asset return, exit interviews/surveys, settlement orchestration, gratuity/leave encashment calculation, documents, alumni, rehire, and absconding automation remain future work.
