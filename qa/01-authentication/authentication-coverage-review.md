# Module 1 — Authentication & Tenant Security: Coverage Review

Generated against `qa/01-authentication/coverage-stats.json` (regenerate with
`python qa/tools/build_test_catalogue.py`). **60/60 functionalities have at least one manual test case** — the
builder enforces this and fails otherwise. This review is the gate before Module 2 (Platform Admin / Tenant
Onboarding) starts: it says where the 243 cases are strong, where they are API-only, and what remains for
later phases.

## 1. Headline numbers

| Metric | Value |
|---|---:|
| Functionalities discovered | 60 |
| Functionalities with ≥1 case | 60 (100%) |
| Total test cases | 243 |
| Total test steps | 632 |
| Smoke candidates | 19 |
| Regression candidates | 242 |
| Security candidates | 93 |
| Cases with an existing-test mapping | 132 (54%) |
| Cases with no existing mapping (new coverage) | 111 (46%) |

By primary type: Positive 64 · Negative 55 · Security 53 · Tenant-Isolation 36 · Boundary 22 · Integration-Concurrency 5 · Authorization 4 · Contract 4.
(Many cases carry a secondary tag — e.g. Negative+Validation — counted once here by primary `type`; see the
workbook's Test Type column for the full tag list per case.)

By recommended automation layer: API 207 (85%) · DB-assisted 16 · UI 16 · API + UI 3 · Manual only 1.
By priority: P0 94 · P1 107 · P2 36 · P3 6.

## 2. Coverage by functionality group

Legend: **cases** = manual cases touching the functionality · **API/UI** = automation-layer split ·
**TenIso/Auth/Sec** = cases tagged Tenant-Isolation / Authorization / Security · **Existing** = cases with an
existing xUnit/Vitest mapping (never live-stack; see §4).

| Functionality | Cases | API | UI | TenIso | Auth | Sec | Existing | Status |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| **Tenant Authentication** | | | | | | | | |
| F-AUTH-01 Credential login | 21 | 21 | 0 | 2 | 4 | 5 | 14 | Implemented |
| F-AUTH-02 Identifier modes / employee code | 7 | 7 | 0 | 1 | 2 | 0 | 3 | Implemented |
| F-AUTH-03 Request validation/envelope | 18 | 18 | 0 | 0 | 0 | 1 | 5 | Implemented |
| F-AUTH-04 Enumeration resistance | 6 | 6 | 0 | 0 | 0 | 5 | 3 | Implemented |
| F-AUTH-05 Account/org status gating | 6 | 6 | 0 | 0 | 1 | 3 | 5 | Implemented |
| F-AUTH-24 Branding/settings admin | 1 | 1 | 0 | 0 | 1 | 0 | 1 | Implemented |
| **Tokens** | | | | | | | | |
| F-AUTH-06 Access token issuance | 9 | 8 | 0 | 1 | 1 | 4 | 6 | Implemented |
| F-AUTH-07 Access token validation | 23 | 22 | 0 | 6 | 1 | 20 | 12 | Implemented |
| F-AUTH-08 Refresh issuance/storage | 11 | 8 | 3 | 0 | 0 | 6 | 7 | Implemented |
| F-AUTH-09 Refresh rotation | 9 | 9 | 0 | 0 | 0 | 3 | 7 | Implemented |
| F-AUTH-10 Replay/theft response | 1 | 1 | 0 | 0 | 0 | 1 | 1 | Implemented |
| F-AUTH-11 Concurrent refresh race | 2 | 2 | 1 | 0 | 0 | 1 | 2 | Implemented |
| F-AUTH-12 Refresh host/tenant binding | 2 | 2 | 0 | 2 | 0 | 2 | 2 | Implemented |
| F-AUTH-13 Refresh revalidation | 4 | 4 | 0 | 0 | 2 | 2 | 4 | Implemented |
| **Session** | | | | | | | | |
| F-AUTH-14 Logout | 7 | 7 | 0 | 1 | 0 | 5 | 5 | Implemented |
| F-AUTH-15 Current user (me) | 13 | 13 | 0 | 2 | 1 | 3 | 4 | Implemented |
| F-AUTH-16 Permission/state freshness | 5 | 5 | 0 | 0 | 4 | 4 | 2 | **Needs clarification (CR-14)** |
| **Password Lifecycle** | | | | | | | | |
| F-AUTH-17 Change password | 5 | 5 | 0 | 0 | 0 | 1 | 2 | Implemented |
| F-AUTH-18 Set initial password | 4 | 4 | 0 | 1 | 0 | 2 | 1 | Implemented |
| F-AUTH-19 Forgot-password identify | 6 | 6 | 1 | 1 | 0 | 4 | 5 | Implemented |
| F-AUTH-20 OTP send/resend | 7 | 7 | 0 | 1 | 0 | 3 | 4 | Implemented |
| F-AUTH-21 OTP verify | 6 | 6 | 0 | 1 | 0 | 3 | 2 | Implemented |
| F-AUTH-22 Password reset | 5 | 5 | 0 | 1 | 0 | 3 | 3 | Implemented |
| F-AUTH-23 Policy/hashing | 6 | 6 | 0 | 0 | 0 | 0 | 3 | Implemented |
| **Tenant Resolution** | | | | | | | | |
| F-TEN-01 Host resolution | 3 | 3 | 0 | 0 | 0 | 0 | 3 | Implemented |
| F-TEN-02 Resolution caching | 1 | 1 | 0 | 0 | 0 | 0 | 1 | Implemented |
| F-TEN-03 Suspended org refused | 3 | 3 | 0 | 0 | 0 | 3 | 3 | Implemented |
| F-TEN-05 Apex/unregistered host | 4 | 4 | 0 | 1 | 0 | 1 | 3 | Implemented |
| F-TEN-07 Forwarded headers | 2 | 2 | 0 | 1 | 0 | 1 | 2 | Implemented |
| **Tenant Isolation** | | | | | | | | |
| F-TEN-04 Token/host agreement | 8 | 8 | 0 | 7 | 0 | 7 | 6 | Implemented |
| F-TEN-08 Client-supplied identity ignored | 2 | 2 | 0 | 2 | 1 | 2 | 2 | Implemented |
| F-TEN-09 Cross-tenant read/update/delete | 13 | 13 | 0 | 13 | 0 | 12 | 3 | Implemented |
| F-TEN-10 Session isolation | 9 | 9 | 0 | 9 | 0 | 5 | 6 | Implemented |
| F-TEN-11 Shard mismatch handling | 3 | 3 | 0 | 1 | 0 | 2 | 3 | Implemented |
| **Platform vs Tenant** | | | | | | | | |
| F-TEN-06 Platform host boundary | 3 | 2 | 1 | 3 | 0 | 1 | 2 | Implemented |
| F-PLT-07 Token separation both ways | 3 | 3 | 0 | 3 | 0 | 3 | 0 | Implemented — **no existing test** |
| **Platform Authentication** | | | | | | | | |
| F-PLT-01 Platform login | 5 | 5 | 0 | 0 | 0 | 3 | 1 | Implemented |
| F-PLT-02 Platform refresh | 3 | 3 | 0 | 0 | 0 | 1 | 0 | Implemented — **no existing test** (CR-16) |
| F-PLT-03 Platform logout | 2 | 2 | 0 | 0 | 0 | 1 | 0 | Implemented — **no existing test** |
| F-PLT-04 Platform current user/revision | 4 | 4 | 0 | 0 | 1 | 3 | 0 | Implemented — **no existing test** (CR-17) |
| F-PLT-05 Platform token validation | 1 | 1 | 0 | 0 | 0 | 1 | 0 | Implemented — **no existing test** |
| F-PLT-06 Platform permission enforcement | 4 | 4 | 0 | 0 | 2 | 3 | 2 | Implemented |
| F-PLT-08 Platform frontend session | 3 | 0 | 3 | 1 | 0 | 1 | 2 | Implemented |
| **Authorization** | | | | | | | | |
| F-AZ-01 Permission policies | 6 | 6 | 0 | 1 | 1 | 3 | 4 | Implemented |
| F-AZ-02 Fallback deny-by-default | 2 | 2 | 0 | 0 | 0 | 2 | 2 | Implemented |
| F-AZ-03 Roles/multi-role union | 7 | 7 | 0 | 0 | 7 | 3 | 6 | Implemented |
| F-AZ-04 401 vs 403 semantics | 4 | 4 | 0 | 1 | 0 | 3 | 3 | Implemented |
| F-AZ-05 Frontend vs backend enforcement | 5 | 2 | 4 | 0 | 1 | 1 | 4 | Implemented |
| F-AZ-06 Redirect-target safety | 1 | 0 | 1 | 0 | 0 | 1 | 0 | Implemented — **no existing test** |
| F-AZ-07 Self-service scoping sample | 2 | 2 | 0 | 1 | 2 | 1 | 0 | **Needs clarification (CR-02, CR-03)** |
| **Transport Security** | | | | | | | | |
| F-SEC-01 Auth rate limiting | 6 | 6 | 0 | 1 | 0 | 1 | 3 | Implemented |
| F-SEC-02 Recovery rate limiting | 1 | 1 | 0 | 0 | 0 | 0 | 1 | Implemented |
| F-SEC-03 CORS | 8 | 8 | 0 | 0 | 0 | 2 | 4 | Implemented |
| F-SEC-04 Security headers | 2 | 2 | 0 | 0 | 0 | 2 | 0 | **Needs clarification (CR-19)** |
| F-SEC-05 Error handling/disclosure | 2 | 2 | 0 | 0 | 0 | 2 | 0 | Implemented — **no existing test** |
| F-SEC-06 Anonymous ops endpoints | 4 | 4 | 0 | 0 | 0 | 1 | 2 | Implemented |
| **Frontend Session** | | | | | | | | |
| F-SEC-07 Storage/remember-me/cross-tab | 4 | 0 | 4 | 0 | 0 | 2 | 4 | Implemented |
| F-SEC-08 Host-aware origin/picker | 2 | 0 | 2 | 1 | 0 | 0 | 2 | Implemented |
| F-SEC-09 Login page behaviour | 4 | 0 | 4 | 0 | 0 | 0 | 4 | Implemented |
| **Provider Parity** | | | | | | | | |
| F-SEC-10 Auth persistence across providers | 2 | 2 | 0 | 0 | 0 | 0 | 2 | Implemented |

## 3. Reading the gaps

- **Backend-only functionalities correctly show 0 UI cases** (e.g. F-AUTH-06/07/09/10, F-TEN-01–11 except the
  two with a UI-observable effect, F-PLT-01–06). This is by design — there is no UI surface to exercise for a
  token-validation rule or a middleware refusal — not a coverage hole.
- **Frontend-only functionalities correctly show 0 API cases** (F-SEC-07/08/09, F-AZ-06, F-PLT-08's UI rows):
  these are client-side behaviours (localStorage, redirect targets, route guards) with no direct HTTP
  contract of their own.
- **Genuine gap — Platform authentication has almost no existing automated coverage.** F-PLT-02/03/04/05 map
  to zero existing xUnit or Vitest tests; only `PlatformTenantAuthorizationTests` touches the platform
  authorization *policy*, not `PlatformAuthController`/`PlatformAuthService` login/refresh/logout/me directly.
  This is the single largest hole this review found: **6 functionalities, 17 test cases, entirely new
  coverage** once automated.
- **F-AZ-06 (redirect-target safety) and F-PLT-07 (token-scheme separation)** also have zero existing
  automated coverage, though each is only 1–3 cases.
- **F-AZ-07 and F-AUTH-16 are marked "Needs clarification"** — not because the case design is incomplete, but
  because running them will produce evidence for a product decision (CR-02, CR-03, CR-14) rather than a
  pass/fail against a documented spec. They are still fully specified, automatable cases.
- **F-SEC-04/F-SEC-05 have zero existing automated coverage** — no xUnit test asserts the security-header
  middleware or the non-Development exception envelope. Both are only reachable in a non-Development-like
  QA deployment for full confirmation (see §5).

## 4. Test-pyramid honesty check

| Existing-coverage layer (from `qa/tools/existing_test_layers.yaml`) | What it proves | What it does NOT prove |
|---|---|---|
| Unit (e.g. `JwtTokenServiceTests`, `PasswordHasherTests`, `CorsOriginPolicyTests`) | Isolated logic is correct | Nothing about the live HTTP pipeline, real MySQL, or a real browser |
| In-process integration (e.g. `AuthEndpointsTests`, `TokenHostAgreementTests`, `TenantIsolationTests`) via `HrmsApiFactory`/SQLite | The real ASP.NET Core pipeline, middleware order and EF query filters work together | SQL Server/MySQL-specific behaviour (collation, concurrency), real network/CORS preflight, real browser storage |
| Component (Vitest + Testing Library, jsdom) (e.g. `AuthProvider.test.tsx`, `LoginPage.test.tsx`, `client.test.ts`) | React component logic and the mocked-adapter Axios interceptor behave correctly | Real network calls, real browser storage partitioning, real multi-tab behaviour |
| Provider acceptance (MySQL/SQL Server, opt-in) | Provider-specific SQL semantics, when the environment variables are set | **Nothing when the environment variables are absent — these tests silently skip and must never be reported as passed** |

**132 of 243 cases (54%) have an existing automated equivalent**, all at the Unit / In-process-integration /
Component layers above. **None of the 243 cases are covered by a live API or live browser/E2E test today** —
that layer does not exist yet (§1.4 of the Phase 1 assessment; the Phase 3B browser acceptance record shows
it has never successfully run). The 111 cases with no existing mapping are not necessarily harder; many are
simply scenarios (crafted-token tampering, live CORS preflight, cross-tenant sweeps, platform auth) that the
existing suite's chosen harness does not reach.

## 5. Environment-dependent cases (do not report as pass without the environment)

- SQL Server provider-parity legs (AUTH-LOGIN-043, AUTH-REFRESH-028): require `HRMS_SQLSERVER_TEST_CONNECTION*`
  or a live SQL Server QA tenant; report `NOT EXECUTED - environment unavailable` if absent.
- Non-Development-mode checks (AUTH-SEC-001, AUTH-SEC-002, AUTH-SEC-007 non-Development leg): require a QA
  deployment running with `ASPNETCORE_ENVIRONMENT` other than `Development`.
- Trusted-proxy forwarding (TENANT-HOST-025): requires a QA deployment with `ForwardedHeaders:KnownProxies`
  or `KnownNetworks` configured.
- SMTP-failure simulation (AUTH-PWD-FORGOT-016): requires an isolated environment where SMTP is deliberately
  unreachable — never the shared/dev SMTP account.

## 6. Clarifications raised while designing this module

CR-01 through CR-20 are registered in `clarifications.yaml`. Ten of them (CR-02, CR-03, CR-14 through CR-20)
were raised specifically while designing Module 1's cases; CR-01, CR-04 through CR-13 carry over from the
Phase 1 repository assessment and are exercised here only where they touch authentication (CR-01 in
AUTH-LOGIN-002). None are classified yet — that is a product-owner decision, not a QA one.

## 7. Recommendation

**Module 1's manual test-case design is complete enough to proceed to Module 2 (Platform Administration /
Tenant Onboarding).** All 60 functionalities discovered in this module have specified, traceable cases; the
cross-tenant framework (two disposable tenants, host/token manipulation patterns) that every later module
will reuse is fully worked out here. The one substantive gap — platform authentication's thin existing
automated coverage — does not block Module 2, because Module 2's own cases will need to exercise
`PlatformAuthService`/`PlatformTenantsController` directly anyway and will close it as a side effect.

Two things worth doing before or during Module 2, not blocking it:
1. Get a product-owner decision on CR-01 (seeded Employee permissions) before writing Employee/Leave/Payroll
   self-service cases, since it changes what "a freshly seeded Employee" can legitimately be asked to do.
2. Confirm whether a non-Development-mode QA deployment will exist, since several Module 1 cases (and likely
   later ones) can only be fully executed there.
