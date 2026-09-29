# Module 1 — Authentication & Tenant Security

Status: manual test-case catalogue complete and validated. No automation implemented yet (Phase 8+, on
explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (55 functionalities: F-AUTH-\*, F-TEN-\*, F-PLT-\*, F-AZ-\*, F-SEC-\*). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-tenant-login.yaml` … `cases/09-tenant-host-resolution-and-cross-tenant.yaml` | The manual test cases, one YAML file per Excel worksheet (`sheet:` key), one case per `id`. |
| `clarifications.yaml` | The CR-01…CR-20 QA-risk register. Each entry has an observed behaviour, a risk statement, a source pointer and a `classification` that starts `Open - awaiting decision`. Cases reference these via `cr: [CR-xx]`; the builder rejects a case that cites an unregistered id. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py`. |

The Excel workbook `qa/HRMS_Test_Cases.xlsx` is generated from these YAML sources across every module that
has been authored so far (currently Module 1 only). The `.xlsx` is never the source of truth.

## Conventions used in every case file

- **Test data is never literal secrets.** Roles are logical names (`A_ADMIN`, `B_HR`, `A_EMP4`) resolved at
  run time from environment variables per `qa/02-test-data/test-data-spec.md` (to be authored alongside the
  automation framework). No password, token, OTP, or connection string appears anywhere in this catalogue.
- **Tenants.** `A` and `B` are two disposable QA tenants provisioned on non-production databases. `S` denotes
  a disposable tenant used only for suspend/inactive scenarios so it never blocks other suites. Case files
  never assume a specific hostname string; hosts come from `QA_TENANT_A_HOST` etc.
- **Steps.** Each step is `[VERB /path] action => [status] expected`. The `[VERB /path]` and `[status]`
  prefixes are optional per step (some steps are setup, DB edits or UI actions with no direct HTTP call) but
  every step has an ` => ` separating the action from its expected result — the builder rejects a step
  without one.
- **`auto` field** is one of exactly: `API`, `UI`, `API + UI`, `DB-assisted`, `Existing automated coverage`,
  `Manual only` — matching the brief's classification scheme.
- **`ex` field** (existing coverage) uses `xU:ClassName.MethodName` for xUnit or `V:relative/path.test.tsx::case name`
  for Vitest. The builder resolves every reference against the real source tree (`Backend/HRMS.Tests`,
  `Frontend/HRMS.Web/src`) and the file **fails to build** if a referenced class, method, file or case name
  does not exist — so this catalogue cannot silently drift from the code as it evolves. Layer classification
  (Unit / Component / In-process integration / Provider acceptance) comes from
  `qa/tools/existing_test_layers.yaml`, not from guessing; **none of these layers is live-stack coverage**.
- **`cr` field** cross-references `clarifications.yaml`. Citing one does not mean the behaviour is a defect —
  it means running the case will produce evidence relevant to a decision that has not been made yet.
- **Provider-gated cases** (SQL Server) say explicitly in their `pre:`/`cm:` fields that an unavailable
  environment must be reported as `NOT EXECUTED - environment unavailable`, never as a pass.

## How to rebuild the workbook

```powershell
python qa/tools/build_test_catalogue.py
```

This validates every case (required fields, controlled vocabularies, functionality coverage, existing-test
references, a light secret-pattern scan) and, only if validation passes, regenerates:

- `qa/HRMS_Test_Cases.xlsx` — `Test_Case_Summary`, one sheet per module area (`Auth_Login`, `Auth_AccessToken`,
  `Auth_Refresh`, `Auth_MeProfile`, `Auth_Password`, `Platform_Auth`, `Authz_Foundation`,
  `Auth_Transport_Security`, `Tenant_Isolation`), `Traceability`, `Existing_Test_Map`, `Clarifications`,
  `Defects` (empty until execution), `Execution_Summary` (empty until execution).
- `qa/01-authentication/authentication-test-cases.generated.csv` — flat step-level export.
- `qa/01-authentication/authentication-traceability.generated.csv` — functionality → cases → existing tests.
- `qa/01-authentication/coverage-stats.json` — the numbers behind `authentication-coverage-review.md`.

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| Auth_Login | 44 | Tenant credential login: identifier modes, validation, enumeration resistance, account/org status, tenant isolation, concurrency, contract |
| Auth_AccessToken | 30 | JWT validation: signature, algorithm, issuer/audience, lifetime, tenant claims, tampering, header size, provider parity |
| Auth_Refresh | 28 | Rotation, replay/theft response, concurrency race, host/tenant binding, logout interaction, storage/remember-me, cross-tab |
| Auth_MeProfile | 13 | `/me`, `/me/profile`, employee-identity states, navigation |
| Auth_Password | 28 | set-password, change-password, forgot-password (identify/OTP/verify/reset), rate limiting |
| Platform_Auth | 25 | Platform login/refresh/logout/me, platform↔tenant token separation, platform permission enforcement, platform host boundary |
| Authz_Foundation | 19 | Permission policies, fallback-deny, 401 vs 403, frontend guards vs backend enforcement, self-service scoping (CR-02/CR-03) |
| Auth_Transport_Security | 29 | Rate limiting (auth + recovery), CORS, security headers, error handling, health/ready/system-info, frontend login/session states |
| Tenant_Isolation | 27 | Host resolution, token/host agreement, client-supplied-identity rejection, cross-tenant sweep across modules, forwarded-header trust |

**Total: 243 test cases** covering 60 functionalities (`python qa/tools/build_test_catalogue.py` regenerates
and reports these numbers; treat `coverage-stats.json` as authoritative over this table if they ever diverge).
