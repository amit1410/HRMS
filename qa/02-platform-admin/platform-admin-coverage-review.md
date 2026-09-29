# Module 2 — Platform Administration / Tenant Onboarding: Coverage Review

Generated against `qa/02-platform-admin/coverage-stats.json` (regenerate with
`python qa/tools/build_test_catalogue.py`). **32/32 functionalities have at least one manual test case** —
the builder enforces this and fails otherwise.

## 1. Headline numbers

| Metric | Value |
|---|---:|
| Functionalities discovered | 32 |
| Functionalities with ≥1 case | 32 (100%) |
| Total test cases | 150 |
| Total test steps | 292 |
| Smoke candidates | 10 |
| Regression candidates | 150 |
| Security candidates | 39 |
| Cases with an existing-test mapping | 52 (35%) |
| Cases with no existing mapping (new coverage) | 98 (65%) |

By primary type: Positive 52 · Negative 47 · Security 19 · Authorization 12 · Tenant-Isolation 8 · Boundary 7 ·
Contract 3 · Integration-Concurrency 2.

By recommended automation layer: API 82 (55%) · DB-assisted 28 (19%) · UI 27 (18%) · Manual only 13 (9%,
almost all of it the `PlatformAdminBootstrap` console tool, which has no HTTP surface to automate against).

By priority: P0 51 · P1 70 · P2 22 · P3 7.

## 2. Coverage by functionality group

Legend: **cases** = manual cases touching the functionality · **API/UI** = automation-layer split ·
**TenIso/Auth/Sec** = cases tagged Tenant-Isolation / Authorization / Security · **Existing** = cases with an
existing xUnit/Vitest mapping (never live-stack; see §4).

| Functionality | Cases | API | UI | TenIso | Auth | Sec | Existing | Status |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| **Tenant Directory** | | | | | | | | |
| F-PLAT-01 List tenants | 5 | 5 | 0 | 1 | 1 | 1 | 1 | Implemented |
| F-PLAT-02 Get tenant detail | 4 | 4 | 0 | 0 | 1 | 0 | 1 | Implemented |
| F-PLAT-31 Web URL construction | 3 | 2 | 1 | 0 | 0 | 0 | 0 | Implemented — no existing test |
| **Tenant Onboarding** | | | | | | | | |
| F-PLAT-03 Create — validation/normalization | 17 | 17 | 0 | 1 | 0 | 2 | 5 | Implemented |
| F-PLAT-04 Create — uniqueness | 5 | 5 | 0 | 0 | 0 | 0 | 0 | Implemented — no existing test |
| F-PLAT-05 Invitation/SMTP gating | 2 | 2 | 0 | 0 | 0 | 0 | 0 | Implemented — no existing test |
| F-PLAT-06 Default branding creation | 1 | 1 | 0 | 0 | 0 | 0 | 1 | Implemented |
| F-PLAT-07 Initial TenantAdmin creation | 4 | 4 | 0 | 0 | 1 | 1 | 1 | Implemented |
| **Provisioning** | | | | | | | | |
| F-PLAT-08 SQL Server provisioning | 6 | 5 | 0 | 2 | 0 | 1 | 6 | Implemented |
| F-PLAT-09 MySQL provisioning | 10 | 9 | 0 | 1 | 0 | 3 | 6 | Implemented |
| F-PLAT-10 MySQL history fail-closed | 6 | 6 | 0 | 0 | 0 | 3 | 4 | Implemented |
| F-PLAT-11 Failure handling | 2 | 1 | 0 | 0 | 0 | 1 | 1 | Implemented |
| F-PLAT-12 Startup provisioning | 3 | 2 | 0 | 0 | 0 | 1 | 2 | Implemented |
| **Tenant Maintenance** | | | | | | | | |
| F-PLAT-13 Update metadata | 11 | 11 | 0 | 1 | 0 | 1 | 1 | Implemented |
| F-PLAT-14 Provisioning identity read-only | 2 | 1 | 1 | 0 | 0 | 2 | 2 | Implemented |
| F-PLAT-15 Reserved-host protection | 1 | 1 | 0 | 0 | 0 | 1 | 0 | **Needs clarification** |
| **Tenant Lifecycle** | | | | | | | | |
| F-PLAT-16 Activate | 6 | 5 | 1 | 1 | 0 | 2 | 0 | Implemented — no existing test |
| F-PLAT-17 Deactivate | 7 | 6 | 1 | 1 | 0 | 3 | 1 | Implemented |
| F-PLAT-18 Suspended (unreachable) | 1 | 1 | 0 | 0 | 0 | 0 | 0 | **Needs clarification** |
| **Tenant Recovery** | | | | | | | | |
| F-PLAT-19 Retry eligibility | 9 | 9 | 0 | 1 | 0 | 3 | 1 | Implemented |
| F-PLAT-20 Retry idempotent admin | 5 | 5 | 0 | 0 | 0 | 1 | 0 | Implemented — no existing test |
| F-PLAT-21 Retry host-format gap | 1 | 1 | 0 | 0 | 0 | 0 | 0 | **Needs clarification** |
| F-PLAT-22 Reset admin password | 15 | 13 | 2 | 1 | 1 | 6 | 8 | Implemented |
| **Security** | | | | | | | | |
| F-PLAT-23 Temp-password exposure | 2 | 2 | 0 | 0 | 0 | 2 | 0 | **Needs clarification** |
| **Authorization** | | | | | | | | |
| F-PLAT-24 Permission enforcement | 11 | 11 | 0 | 1 | 7 | 4 | 4 | Implemented |
| F-PLAT-25 Create host-protection gap | 1 | 1 | 0 | 0 | 0 | 0 | 0 | **Needs clarification** |
| F-PLAT-32 Platform-host boundary | 6 | 6 | 0 | 6 | 0 | 6 | 0 | Implemented — no existing test |
| **Bootstrap** | | | | | | | | |
| F-PLAT-26 Bootstrap tool | 10 | 0 | 0 | 0 | 0 | 4 | 0 | Implemented — no existing test |
| **Catalog Schema** | | | | | | | | |
| F-PLAT-27 Schema isolation | 3 | 2 | 0 | 0 | 0 | 2 | 2 | Implemented |
| **Frontend** | | | | | | | | |
| F-PLAT-28 Tenants page list/filter | 5 | 0 | 5 | 0 | 1 | 0 | 2 | Implemented |
| F-PLAT-29 Tenants page CRUD/dialogs | 15 | 0 | 15 | 0 | 3 | 2 | 10 | Implemented |
| F-PLAT-30 Platform login page | 5 | 0 | 5 | 0 | 0 | 1 | 5 | Implemented |

## 3. Reading the gaps

- **Backend-only functionalities correctly show 0 UI cases** (provisioning, catalog schema, bootstrap,
  authorization internals) — there is no UI surface for a MySQL migration-history check or a permission
  policy, not a coverage hole.
- **Frontend-only functionalities correctly show 0 API cases** (F-PLAT-28/29/30) — client-side rendering,
  filtering and dialog behaviour have no direct HTTP contract of their own.
- **Genuine gap — the `PlatformAdminBootstrap` tool has zero existing automated coverage and cannot be API-
  automated at all.** It is an interactive console app with no non-interactive mode in source, so all 10
  cases are `Manual only`. This is the single largest gap this review found for Module 2, and it is
  structural rather than a testing-effort shortfall: Playwright/API automation has nothing to call.
- **F-PLAT-04 (uniqueness), F-PLAT-05 (invitation gating), F-PLAT-16 (Activate), F-PLAT-20 (retry admin
  idempotency), F-PLAT-32 (platform-host boundary reused across tenant endpoints) have zero existing
  automated coverage.** Existing xUnit tests validate the *shape* of Create/Retry/Update requests and the
  *permission attributes* on the controller, but no in-process test exercises the create→provision→activate
  happy path, the duplicate-identity 409s, the Activate endpoint itself, or retry's admin-reuse branch.
- **F-PLAT-15, F-PLAT-18, F-PLAT-21, F-PLAT-23, F-PLAT-25 are marked "Needs clarification"** — each is fully
  specified and automatable, but running it produces evidence for a product decision (CR-15/CR-18/CR-21/
  CR-22/CR-23/CR-25/CR-26, see §5) rather than a pass/fail against a documented spec.
- **DB-assisted cases (28, 19%) are unusually high for this module** relative to Module 1, because tenant
  lifecycle testing inherently needs to inspect or manipulate catalog/shard rows directly (Suspended status,
  broken admin accounts, pre-existing MySQL schemas, multiple-TenantAdmin scenarios) that no API endpoint
  can produce on its own.

## 4. Test-pyramid honesty check

Same layer definitions as Module 1 (`qa/tools/existing_test_layers.yaml`); nothing new to add here except
that this module's existing coverage skews further toward Unit-level reflection/validator tests
(`PlatformTenantAuthorizationTests`, `PlatformTenantRequestValidatorTests`,
`PlatformTenantRecoveryValidatorTests`) and away from HTTP-level in-process integration — there is no
`PlatformTenantsController`-level `WebApplicationFactory` test suite equivalent to Module 1's
`AuthEndpointsTests`. **52 of 150 cases (35%) have an existing automated equivalent, all at the Unit,
In-process-integration, Provider-acceptance (MySQL, opt-in) or Component layers. None of the 150 cases are
covered by a live API or live browser/E2E test today** — same as Module 1, that layer does not exist yet in
this repository.

## 5. Environment-dependent cases (do not report as pass without the environment)

- SQL Server provisioning legs (PLAT-PROV-001, PLAT-PROV-016, PLAT-BOOT-010's SQL Server leg): require
  `HRMS_SQLSERVER_TEST_CONNECTION*` or an equivalent disposable SQL Server QA target.
- Non-Development-mode checks (PLAT-CREATE-019, PLAT-RETRY-006, PLAT-RETRY-010, PLAT-PWDRESET-002): require
  a QA deployment running with `ASPNETCORE_ENVIRONMENT` other than `Development`.
- Frontend production-build check (PLAT-PWDRESET-014): requires both a dev-mode and a production build of
  the frontend pointed at the same API.
- Several DB-assisted cases require direct write access to a disposable QA catalog or tenant shard database
  (Suspended status, broken/multiple TenantAdmin accounts, pre-existing MySQL schemas with foreign or
  partial migration history) — never a shared or production database, per your standing instruction.

## 6. Clarifications raised in this module

CR-21 through CR-27 are new to this module; CR-14 is a cross-module pointer back to Module 1's register,
included here because `PLAT-STATUS-006` shows the same stale-access-token exposure class in the specific
context of an operator deactivating a whole tenant. None are classified yet — that is a product-owner
decision. Two are worth flagging as higher-priority than the rest:

- **CR-25 (Suspended is unreachable dead code/UI)**: `TenantStatus.Suspended` is guarded against in
  `SetStatusAsync` and offered as a filter in the frontend, but nothing in the repository ever sets it. This
  should be resolved (implement it, or remove the guard/filter) before it is mistaken for a working feature
  during a later module's testing.
- **CR-27 (admin password reset doesn't revoke refresh tokens)**: unlike every self-service password change
  in Module 1, this platform-operator action leaves existing sessions live. Worth a security decision before
  this endpoint is relied on as an incident-response tool.

## 7. Recommendation

**Module 2's manual test-case design is complete enough to proceed to the next module.** All 32
functionalities have specified, traceable cases; the tenant-lifecycle state machine (Inactive → Active,
Active ⇄ Inactive, the Retry path, the unreachable Suspended state) is fully worked out and will inform how
later modules treat tenant status as a precondition. The one structural gap — the bootstrap tool's complete
lack of automatability — is inherent to the tool's design, not a test-design shortfall, and does not block
further work; it is simply carried forward to Phase 13 (defect/ops-runbook territory) as a manual-only,
regression-only check.

Two things worth doing before or during the next module, not blocking it:
1. Get a product-owner decision on CR-25 (Suspended) and CR-18 (platform login rate limiting, from Module 1)
   together, since both concern the platform administration surface's production-readiness.
2. If Module 3 touches Masters/Employee data, note that every onboarded tenant used for that module's tests
   should come from this module's Create flow (or an equivalent fixture) so cross-tenant setup stays
   consistent with how a real tenant actually enters the system.
