# Module 6 — RBAC / Role Management / Account↔Employee Linking / Access Control

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py`
regenerates `qa/HRMS_Test_Cases.xlsx` across all six modules with no errors). No automation implemented yet
(Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (185 functionalities: F-RBAC-001…185). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-role-inventory-permissions.yaml` … `cases/11-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet — see the table below. |
| `clarifications.yaml` | The CR-66…CR-76 QA-risk register for this module, plus short cross-references to CR-44 (Module 4), CR-53 and CR-57 (Module 5) that this module's cases exercise directly. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here.

## The real implementation (source-verified, not assumed from the brief's example role list)

Discovered by reading `RoleAssignmentsController`, `AccountEmployeeLinksController`, `PageAccessController`
(+ `NavigationController`), `MeController`, `RoleAssignmentService`, `AccountEmployeeLinkService`,
`PageAccessService`, `RoleResolutionService`, `RoleScopeResolver`, `EmployeeRoleProvisioningService`,
`ManagerRoleProvisioningService`, `AuthService` (`GetCurrentUserAsync`/`IssueTokensAsync`/
`ResolveEmployeeIdentityAsync`/`LoadAuthorizationAsync`), `EmployeeIdentityResolver`,
`Domain/Authorization/Permissions.cs`, `RoleNames.cs`, every `UserRole*`/`AccountEmployeeLink*`/
`AuthorizationConfigurationEvent` entity and enum, `ApplicationPageCatalog`, `SeedData.cs` (`RoleIds`,
`PermissionIds`, `RolePermissionMap`), the frontend `RoleManagementPage.tsx`, `PageAccessManagementPage.tsx`,
`AccountEmployeeLinksPage.tsx`, `auth/permissions.ts` and `layout/navigation.ts`, and every existing xUnit/
Vitest file touching this surface.

**There are 14 seeded roles, all fixed with hardcoded ids — there is no Role Master create/edit/delete API
anywhere.** `RoleAssignmentsController` exposes only `GET .../roles` (list); the only thing about a role that
is actually editable is its *permission set*, through `PageAccessController`'s `PUT /api/page-access/roles/
{roleId}`. Roles: SuperAdmin, TenantAdmin, HRAdmin, HRManager, Manager, Employee, AccountLinkAdministrator,
AccountLinkAuditor, EmployeeRelationshipOfficer (ERO), HRBP, TimeManager, IT, Accounts, SuperHR. Employee and
Manager are **system-managed** — `RoleAssignmentService.AssignAsync`/`RevokeAsync` explicitly reject a manual
assign/revoke of either.

**The single highest-impact finding in this module (CR-66):** `PageAccessService.UpdateAsync`, gated only by
`PageAccess.Manage`, lets the caller replace **any** role's **entire** permission set with **any** subset of
`Permissions.All` — no check that the caller already holds the permissions being granted, no restriction on
which role id is targeted (including SuperAdmin's or TenantAdmin's own row), and **no carve-out for
`RoleManagement.*` or `AccountEmployeeLink.*`** — the very family SuperAdmin/TenantAdmin are deliberately
denied (`RolePermissionMap[SuperAdmin/TenantAdmin] = DomainPermissions.All.Where(x =>
!x.StartsWith("AccountEmployeeLink."))`). `PageAccess.Manage` is held by SuperAdmin, TenantAdmin, HRAdmin and
SuperHR. HRAdmin holds **no** `RoleManagement.*` permission at all, and SuperHR holds only `RoleManagement.
View`/`.AssignmentView` (not `.AssignmentManage`) — both can nonetheless reach full role administration *and*
full Account-Employee-Link control, in one authenticated `PUT` call each, by granting their own role the
permissions it is missing. The product's own `AuthorizationMatrixTests` already treats `PageAccess.Manage` as
equivalent to `RoleManagement.Manage` for its "role administration" boundary assertion — the escalation path
is not a subtle interpretation, it is the same equivalence the test suite itself encodes.

**Two independent auto-provisioning services exist**, both additive-only. `EmployeeRoleProvisioningService`
grants the seeded Employee role the moment an account is Linked or Replaced to an employee — but **not** on
Unlink (CR-69), and Employee creation alone never triggers it (F-RBAC-055). `ManagerRoleProvisioningService`
grants the seeded Manager role the moment an Employment History change names a *currently-effective* manager
— but its own "is this employee currently a manager" probe checks only `EffectiveTo>=today`, with **no**
`EffectiveFrom<=today` lower bound, so a future-dated promotion grants the role **today** (CR-70). Neither
role, once granted, is ever revoked by any code path — both services' own doc comments/behavior confirm this
is deliberate (CR-71), and the Manager grant fires from exactly one call site
(`EmployeeEmploymentService.CreateChangeAsync`), never from the legacy full-record `PUT /api/employees/{id}`
path, and never via any startup reconciliation (CR-72).

**Account↔Employee Linking is optimistic-concurrency controlled and self-service-prohibited.** Every
mutation (Link/Unlink/Replace) requires the caller's own `expectedRevision` (the latest link-event id) to
match, and an operator **cannot change their own account's link** — a hard, unconditional 403, confirmed both
server-side and by the frontend's account picker filtering out the signed-in user. The three
`AccountEmployeeLink.*` permissions are deliberately excluded even from SuperAdmin/TenantAdmin's otherwise
universal grant — confirmed to hold through the normal role-assignment path, but (per CR-66) trivially
defeated through Page Access Management, which has no equivalent carve-out.

**CR-53 (carried forward from Module 5) has direct RBAC consequences, confirmed here.** `User.IsActive` is
completely independent of the linked `Employee.Status` — so a terminated employee's account keeps every role
it held (including the auto-granted Employee role, which cannot even be *manually* revoked since it is
system-managed), `GET /api/auth/me` keeps reporting `EmployeeIdentity.Status='Linked'` (with
`EmploymentEligibility='Separated'`), and the Roles/Permissions on that same response are computed with zero
reference to employment eligibility at all.

**Scope is two unrelated mechanisms wearing the same name.** This module's own `RoleScopeResolver` +
`UserRoleAssignmentScope` handles HRBP/ERO/TimeManager dimension-based scoping (Department, Grade, Cost
Center, etc. — 16 dimensions, ANDed across dimensions, ORed within one). The Manager role's own
self+direct-reports narrowing (Module 5's CR-57) is a **completely separate** mechanism living in
`EmployeeAccessScopeService`, keyed off an *Attendance* permission (`Attendance.Monthly.ViewTeam`) plus the
caller's linked-employee id — a Manager-role assignment carries **zero** `UserRoleAssignmentScope` rows. The
same Attendance-permission signal resurfaces as the Page Access preview's "Manager access" field (CR-74).

**Page Access Management edits shared, tenant-agnostic reference data.** `Role`/`Permission`/`RolePermission`
carry no `TenantId` — a permission grant made from one tenant's admin session applies to the *same shared
role row* every other tenant using that role also reads from (CR-75). Separately, the backend's own page
catalog (`ApplicationPageCatalog`, 9 pages) and the frontend's actual sidebar (`layout/navigation.ts`
`NAV_ITEMS`, 50+ items) are two independent, never-reconciled catalogs — `GET /api/auth/navigation` does not
drive the sidebar at all (CR-73).

## Coverage matrix — which behavior is exercised where

| Behavior | Fully exercised on | Spot-checked on | Kind-specific cases |
|---|---|---|---|
| Role Master / Permission Matrix | list/assignableOnly, absence of create/edit/delete, grant/revoke via Page Access, audit trail | stale-token vs. `/api/auth/me` freshness, permission union/dedup | CR-66 privilege-escalation proof (HRAdmin and SuperHR both) |
| User-Role Assignment | assign/revoke, overlap/adjacency, system-role rejection, SuperHR special rules, scope validation | frontend scope-picker gap (CR-67), self-assignment (CR-68), concurrent race | manipulated roleId/userId, cross-tenant role id (a non-issue, roles are shared) |
| Default Employee Role | grant-on-link, no duplicate, no grant on Employee creation alone | **unlink does not revoke (CR-69), no manual-revoke path** | CR-53 terminated-employee-keeps-role |
| Manager Role auto-provisioning | grant-on-employment-change, no duplicate across multiple reports | **future-dated grant fires today (CR-70)**, additive-forever (CR-71), legacy-path never triggers it (CR-72) | no reconciliation job exists |
| Account↔Employee Linking | link/unlink/replace, eligibility rules, self-service prohibition, optimistic concurrency | orphaned-link Invalid state, candidate-list asymmetry | concurrent link/replace/unlink races |
| Link History / Replacement | append-only, sequence-numbered, Replace's before+after pair | cross-tenant history gap (CR-76) | correlation id per mutation |
| Page Access | 9-page fixed catalog, matrix grant/revoke, navigation, preview, history | **backend/frontend nav catalogs disagree (CR-73)**, "Manager access" is an Attendance-permission alias (CR-74) | **shared-role tenant leak (CR-75)** |
| Scope Authorization | dimension AND/OR semantics, zero-scope=tenant-wide, no-employment=never-applies | **Manager's CR-57 mechanism is entirely separate from this module's own scope resolver** | role-combination union tests (Employee+Manager, +HRBP, HRAdmin+Manager, etc.) |
| Security / Tenant Isolation | full role×permission matrix, self-service identity, CR-53's RBAC-side confirmation | **CR-66 escalation proven for both HRAdmin and SuperHR**, consolidated Tenant A/B matrix | AccountEmployeeLink carve-out holds normally, defeated via Page Access |
| Concurrency / Audit | double-revoke idempotency, SuperHR-last-one-standing race, page-access concurrent edit | immutability sweep (no PUT/DELETE on any audit trail) | correlation-id uniqueness |
| Frontend | tab lazy-loading, aggregate server filtering, permission-gated controls, error/empty/forbidden states | direct-route-guard enforcement, bulk page-toggle composition | RevokeDialog date floor, preview panel composition |

## Sheets produced for this module

| Sheet | Cases | Steps | Focus |
|---|---:|---:|---|
| RBAC_Roles | 22 | 56 | Role Master (list-only), the full permission matrix, grant/revoke audit, stale-token vs. live `/me`, **CR-66 privilege escalation** |
| RBAC_Assignments | 31 | 63 | Assign/revoke, overlap/scope validation, SuperHR rules, history, cross-tenant, concurrency |
| RBAC_EmployeeRole | 11 | 33 | Default Employee role auto-provisioning, **CR-69 (unlink doesn't revoke)**, CR-53 confirmation |
| RBAC_ManagerRole | 9 | 26 | Manager role auto-provisioning, **CR-70 (future-dated fires today)**, CR-71/CR-72 (never revoked, legacy path gap) |
| AccountLink_Current | 28 | 57 | Link/unlink/replace, eligibility, self-service prohibition, the View+Manage stacked-permission carve-out |
| AccountLink_History | 11 | 21 | Append-only history, Replace's audit shape, **CR-76 tenant-filter gap**, link concurrency |
| RBAC_PageAccess | 14 | 26 | 9-page catalog, matrix, navigation, preview, history, **CR-73/CR-74/CR-75** |
| RBAC_Scopes | 17 | 26 | RoleScopeResolver dimension semantics, **CR-57's separate mechanism**, 8 role-combination union cases |
| RBAC_Security | 19 | 30 | Self-service identity (`/api/auth/me`), **CR-53 in depth**, consolidated privilege-escalation and Tenant A/B matrix |
| RBAC_ConcurrencyAudit | 6 | 14 | Page Access concurrent edit, audit-trail immutability sweep, SuperHR-last-one race |
| RBAC_Frontend | 17 | 28 | Role Management/Page Access/Account-Links page behavior, permission-gated UI states |

**Total: 185 test cases, 380 steps, 185 functionalities** (`python qa/tools/build_test_catalogue.py`
regenerates and reports these numbers; treat `coverage-stats.json` as authoritative over this table if they
ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-66 (the single highest-impact finding)**: `PageAccess.Manage` lets HRAdmin or SuperHR self-grant full
  role administration and `AccountEmployeeLink.Manage`, defeating both the `RoleManagement.AssignmentManage`
  gate and the deliberate `AccountEmployeeLink.*` carve-out, in one `PUT` call each.
- **CR-67**: The frontend's HRBP/ERO/TimeManager scope-type picker offers fewer dimensions than the backend
  accepts (SubDepartment/Section/SubSection/Function/SubFunction/Country are backend-legal but UI-unreachable).
- **CR-68**: `RoleAssignmentService` has no general self-assignment guard outside SuperHR's own narrow rule —
  unlike `AccountEmployeeLinkService`'s unconditional self-link prohibition.
- **CR-69**: Unlinking an account does not revoke its previously auto-provisioned Employee role, and there is
  no supported path to remove it manually (Employee is system-managed).
- **CR-70**: A future-dated employment change naming a manager provisions the Manager role *today*, not on
  the change's own effective date.
- **CR-71**: Manager-role provisioning is permanently additive — no code path ever removes it.
- **CR-72**: Manager-role provisioning fires only from the Employment History endpoint, never the legacy
  full-record edit path, and no startup reconciliation exists.
- **CR-73**: The backend's 9-page `ApplicationPageCatalog` and the frontend's actual sidebar
  (`layout/navigation.ts`) are independent, disagreeing catalogs — the sidebar does not consume `GET
  /api/auth/navigation`.
- **CR-74**: The Page Access preview's "Manager access" field reuses Module 5's CR-57 Attendance-permission
  signal, not any RoleManagement-owned concept of "is a manager."
- **CR-75**: Page Access Management edits a shared, tenant-agnostic role-permission catalog — a change from
  one tenant's session applies to every tenant using that role.
- **CR-76**: `AccountEmployeeLinkService.GetHistoryAsync` filters by `SubjectUserId` only, with no explicit
  `TenantId` clause, unlike the equivalent Role Assignment history query.

`clarifications.yaml` also carries short cross-references to **CR-44** (Module 4, HRAdmin's Employee Code
Configuration gap), **CR-53** (Module 5, terminated employees keep an active login — this module confirms the
RBAC-side consequences directly) and **CR-57** (Module 5, the Manager role's Attendance-permission-coupled
scope narrowing) — each is a pointer back to the originating module's full write-up, re-declared only so a
Module 6 case's `cr:` field resolves against this module's own registry.

None of these are asserted as confirmed defects — each is exposed by specific test cases and awaits a
product decision, per the classification column in `clarifications.yaml`.

## Existing automated coverage referenced by this module

xUnit: `RoleAssignmentServiceTests`, `RoleAssignmentConcurrencyTests` (MySQL, opt-in, skips silently without
`HRMS_MYSQL_TEST_CONNECTION`), `RoleResolutionServiceTests`, `RoleScopeResolverTests`,
`MySqlPageAccessAuthorizationIntegrationTests` (MySQL, opt-in), `AccountEmployeeLinkServiceTests`,
`AccountEmployeeLinkEndpointTests`, `AuthorizationMatrixTests`, `SeedDataTests`. Vitest:
`RoleManagementPage.test.tsx`, `PageAccessManagementPage.test.tsx`, `AccountEmployeeLinksPage.test.tsx`.
`qa/tools/existing_test_layers.yaml` was extended (additively) to classify the five xUnit and two Vitest
files this module newly references; none of Modules 1–5's existing entries were changed. A green run of the
two MySQL-tagged classes is **not** evidence of parity if `HRMS_MYSQL_TEST_CONNECTION` was absent — they skip
silently per `CLAUDE.md`'s documented convention.
