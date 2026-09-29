# Module 2 — Platform Administration / Tenant Onboarding

Status: manual test-case catalogue complete and validated. No automation implemented yet (Phase 8+, on
explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (32 functionalities: F-PLAT-01…32). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-tenant-listing-and-detail.yaml` … `cases/09-frontend-platform-admin.yaml` | The manual test cases, one YAML file per Excel worksheet (`sheet:` key), one case per `id`. |
| `clarifications.yaml` | The CR-14 (cross-module pointer) and CR-21…CR-27 QA-risk register for this module. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here.

## Scope and boundary with Module 1

Module 1 already covers platform *authentication* (login/refresh/logout/me, platform-vs-tenant token
separation, the platform host boundary) in depth — see its `Platform_Auth` sheet. This module covers
everything Module 1 deliberately left out: the tenant lifecycle itself (create, provision, update, activate/
deactivate, retry, reset-admin-password), the database provisioning it triggers, the platform-admin
bootstrap tool, and the `PlatformTenantsPage` UI. A few cases here re-verify the platform-host boundary
(`F-PLAT-32`) and permission-live-revocation behaviour specifically against the tenant-management endpoints,
cross-referencing rather than duplicating Module 1's fuller treatment.

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| Platform_Tenant_List | 11 | List/Get, web-URL construction, platform-host boundary |
| Platform_Tenant_Create | 29 | Onboarding validation, normalization, uniqueness, provider support, default branding, initial-admin creation, failure handling |
| Platform_Provisioning | 16 | SQL Server/MySQL database provisioning, migration-history fail-closed checks, startup provisioning, provider isolation |
| Platform_Tenant_Update | 14 | Metadata edit, reserved-host protection, provisioning-identity immutability, concurrency |
| Platform_Tenant_Status | 11 | Activate/deactivate, idempotency, the unreachable Suspended state, permission separation |
| Platform_Tenant_Retry | 15 | Retry eligibility, identity re-validation, idempotent admin recovery, the Development-host-format gap |
| Platform_Tenant_PasswordReset | 14 | Development-only reset, zero/one/many-admin selection, session-revocation gap |
| Platform_Bootstrap_Authz | 15 | The `PlatformAdminBootstrap` console tool, cross-endpoint authorization, catalog/tenant model isolation |
| Platform_Frontend | 25 | `PlatformLoginPage` and `PlatformTenantsPage`: permission-gated rendering, forms, dialogs, confirmations |

**Total: 150 test cases** covering 32 functionalities (`python qa/tools/build_test_catalogue.py` regenerates
and reports these numbers; treat `coverage-stats.json` as authoritative over this table if they ever
diverge).
