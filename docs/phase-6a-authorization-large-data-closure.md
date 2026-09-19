# Phase 6A authorization and large-data closure

Status: **COMPLETE**

Production deployment, Ubuntu/Nginx acceptance, production backup/restore, and
production tenant lifecycle acceptance are intentionally deferred to the later
production-readiness milestone.

## Authorization architecture

```text
Authentication
    ↓
Page/API permission
    ↓
Authoritative Account ↔ Employee identity where required
    ↓
Effective manager or organizational scope
    ↓
Tenant isolation
```

Frontend navigation and route guards are convenience controls only. API
authorization and server-side employee-scope predicates remain authoritative.

## Role and scope evidence

The centralized `AuthorizationMatrixTests` covers Employee, Manager, HRBP,
Employee Relationship Officer, Time Manager, IT, Accounts, HRAdmin, SuperHR,
and TenantAdmin. Existing runtime suites cover authoritative employee identity,
effective manager scope, organizational scope, Page Access, Leave, Attendance,
Role Management, unlinked users, and cross-tenant denial.

Key results:

- Employee self-service is authoritative-link and permission gated.
- Manager access is effective-relationship and scope gated.
- HRBP/ERO/Time Manager access follows configured scope.
- IT and Accounts receive no automatic Leave/Attendance/HR elevation.
- HR Admin/SuperHR/TenantAdmin remain limited to their authenticated tenant.
- Page visibility does not substitute for API authorization.
- Unlinked accounts cannot use employee self-service.
- Tenant isolation overrides role and business scope.

## Large-data audit

- Attendance exception derivation applies authorization before employee-ID
  materialization and uses set-based period queries. Derived exceptions remain
  bounded to authorized employees and one validated period.
- Attendance manager regularization and On Duty queues apply scope before
  count, deterministic ordering, paging, and page hydration.
- Leave pending approvals apply authorization, count, ordering, `Skip`, and
  `Take` in the database.
- Leave balances apply authorization and organizational filters in the database;
  count, ordering, and paging are server-side. Employment history is loaded
  only for returned rows. Unlimited types are a small query-side union.
- Reviewed employee, Leave, Attendance, Role, Page Access, calendar, and report
  paths do not load an unrestricted tenant dataset before authorization.
- Export/report aggregations retain explicit bounds and operate only on
  authorized filtered data.

## Verification

- Backend: 1,257 passed, 66 skipped, 0 failed (1,323 total).
- Authorization matrix: 12 passed, 0 failed.
- Focused Attendance/Leave/matrix regression: 36 passed, 0 failed.
- SQL Server: post-change verification PASS using the dedicated disposable
  `HRMS_Phase6A_IntegrationTest_20260919` database. Migration, provider,
  authorization, Leave, Attendance, and updated query-path verification
  completed successfully. The SQL Server verification blocker is resolved.
- MySQL: focused Leave report and prior Attendance, Leave, Role, and Page Access
  verification passed.
- Frontend: 573 passed across 72 files; TypeScript PASS; production build PASS;
  lint PASS with existing warnings.
- Backend source/test compilation: PASS. The special local
  `dotnet build --no-restore` workload-resolver issue is an environment/toolchain
  issue only and produced no source compiler errors.
- `git diff --check`: PASS, with normal line-ending warnings only.

## Changed files

- `Backend/HRMS.Application/Services/AttendanceMonthlyProcessor.cs` — bounded,
  set-based Attendance exception derivation.
- `Backend/HRMS.Application/Services/AttendanceWorkflowService.cs` — scoped,
  server-side manager queue paging.
- `Backend/HRMS.Application/Services/LeaveReportService.cs` — server-side Leave
  balance and pending-report filtering and paging.
- `Backend/HRMS.Tests/AuthorizationMatrixTests.cs` — centralized role matrix.
- `docs/phase-6a-authorization-large-data-closure.md` — closure evidence.

## Deferred production work

- Production upload/deployment.
- Ubuntu/systemd/Nginx acceptance.
- Production SQL Server/MySQL backup and restore.
- Production tenant lifecycle and smoke testing.

PHASE 6A: COMPLETE
