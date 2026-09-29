# HRMS QA Automation Framework

Python + Playwright + pytest + Allure automation for HRMS, built alongside the manual QA
catalogue in `qa/*/cases/*.yaml`. This directory is purely additive: **no HRMS application code
was modified to produce or harden it**, and nothing here is committed automatically — that stays
a manual, explicit step for whoever owns the branch.

## Status

Proof-of-concept, now covering five modules: Authentication (Module 01, hardened — real generated
Allure HTML report, redacted failure evidence, opt-in retry policy scoped to transient failures
only), a first, deliberately narrow Employee Management smoke batch (Module 05), a first,
deliberately narrow Leave Management smoke batch (Module 07), a first, deliberately narrow
Attendance Management smoke batch (Module 08 — added on explicit approval, per the "do not start
Attendance without a deliberate decision" note this section used to carry), and a first,
deliberately narrow Payroll smoke batch (Module 10, plus one representative Bank Advice case from
Module 15 and one Analytics case from Module 17 — Phase 7, see below). Do not scale any of these
five out further without a deliberate decision — see "Remaining setup / blockers" below.

## Structure

```
qa/automation/
  config/          Environment configuration (config/settings.py) — base URLs, ports, from env vars
  core/            Framework internals: HTTP client (core/api_client.py), login/session helpers
                   (auth_helpers.py), Employee module endpoint wrappers (employee_api.py), Leave
                   module endpoint wrappers (leave_api.py), Attendance module endpoint wrappers
                   (attendance_api.py), Payroll module endpoint wrappers — Salary Component/
                   Structure/Employee Salary Assignment/Periods/Runs/Inputs/Loans/Variable Pay/
                   Bank Advice/GL Accounting/Analytics (payroll_api.py)
  pages/           Page Object Model — one class per screen (login_page.py, dashboard_page.py,
                   employees_list_page.py, employee_form_page.py, my_leave_requests_page.py,
                   my_attendance_page.py, attendance_requests_page.py) — Payroll (Phase 7) is
                   API-first per its brief; no new Page Objects were added
  tests/
    ui/            Playwright browser tests
    api/           Direct HTTP API tests (no browser)
  data/            Reusable test-data utilities — env-driven credential loader, known-invalid
                   values, unique-data generators (test_data.py)
  utils/           Logging (logger.py), env helpers (env_utils.py), redaction (redact.py)
  reports/         Allure results/report output (git-ignored)
  .tools/          Portable JRE + Allure commandline, downloaded locally (git-ignored — see below)
  conftest.py      Shared fixtures (settings, api_client_factory, admin_api_client,
                   employee_api_client, manager_api_client, created_employee, ui_signed_in,
                   ui_signed_in_employee) + Allure/environment/evidence hooks
  pytest.ini       Markers, default addopts (screenshot/trace on failure, Allure dir)
  run_allure.ps1   Wrapper that points the portable Allure CLI at the portable JRE
  requirements.txt
  .env.example     Copy to .env (git-ignored) and fill in for your disposable QA environment
```

Adding a module later means adding `pages/<module>_page.py` + `tests/ui|api/test_<module>_*.py`;
the config/core/utils layers are already module-agnostic.

## Packages used

- **pytest** — test runner, markers, fixtures
- **pytest-playwright** — Playwright's own pytest plugin: `page`/`browser`/`context` fixtures,
  `--screenshot`, `--tracing`, `--browser` CLI flags
- **playwright** — browser automation (Chromium installed; Firefox/WebKit supported by the same
  code via `--browser firefox|webkit`, just not installed by default)
- **allure-pytest** — Allure result generation, `@allure.feature/story/title`, `allure.step`,
  `allure.attach`
- **pytest-xdist** — parallel execution (`-n auto`)
- **pytest-rerunfailures** — opt-in retries, scoped by `--only-rerun` to transient errors (see
  "Retries" below); never retries an assertion failure
- **python-dotenv** — loads `.env` for local runs
- **requests** — HTTP client for the `tests/api` layer

## Setup

```powershell
cd qa/automation
python -m venv .venv
.venv\Scripts\Activate.ps1

# install dependencies
pip install -r requirements.txt

# install Playwright browsers (Chromium only, to start)
playwright install chromium
# Firefox/WebKit, when needed:
playwright install firefox webkit
```

Copy `.env.example` to `.env` and fill in `QA_TENANT_A_HOST` and `QA_A_ADMIN_USERNAME` /
`QA_A_ADMIN_PASSWORD` (a caller with at least `Employee.View`/`.Create`/`.Edit`/`.Delete`) for
your disposable QA tenant; `QA_A_EMPLOYEE_USERNAME`/`QA_A_EMPLOYEE_PASSWORD` (a caller holding
only the plain, self-service `Employee` role, linked to an actual Employee record) is needed for
the Employee-module authorization-denial test **and** for every Leave self-service test (Leave's
self-service endpoints resolve the caller's own linked Employee identity — an admin/TenantAdmin
seed user is not necessarily linked to one); `QA_A_MANAGER_USERNAME`/`QA_A_MANAGER_PASSWORD` (a
caller holding the `Manager` role, which grants `Leave.Approve`) is needed for the one Leave
approval-inbox test. **Never** put real/production credentials here or in any test file — tests
that can't find the variable they need `pytest.skip()` with a clear reason instead of failing or
fabricating a result. See "Credential handling" below for how these are (and aren't) used.

## Running

All commands assume `cd qa/automation` and the venv is active (or call
`.venv\Scripts\python.exe -m pytest ...` directly without activating).

```powershell
# run the smoke suite
pytest -m smoke

# run headed (visible browser)
pytest -m smoke --headed

# run on Chromium explicitly (also the default)
pytest -m smoke --browser chromium

# other useful filters
pytest -m "ui and smoke"
pytest -m "api and smoke"
pytest -m critical
pytest -n auto -m smoke          # parallel, once tests are known-independent

# verify Firefox/WebKit collection without installing their browser binaries
pytest --browser firefox --collect-only
pytest --browser webkit --collect-only

# retry only transient/infrastructure failures (never assertion failures — see "Retries")
pytest -m smoke --reruns 2 --only-rerun "ConnectionError|ConnectTimeout|Timeout|ECONNREFUSED|net::ERR"
```

Screenshot-on-failure and trace-on-failure are on by default (`pytest.ini`
`--screenshot=only-on-failure --tracing=retain-on-failure`); artifacts land under
`test-results/`. See "Failure evidence" below for what else gets attached automatically.

## CI

`.github/workflows/qa-automation.yml` runs this suite on manual trigger, on a self-hosted runner
next to the local stack. It has a collection check, blocking `smoke` (`not known_defect and not
e2e`) and `e2e` jobs, and non-blocking `known-defects` and Allure report jobs. Every step is a
`python ci/run_ci.py ...` command that you can also run locally. CI stays under the auth rate limit
in three ways: it runs modules sequentially, it reuses one API sign-in per identity
(`QA_REUSE_API_SESSIONS`), and it paces tests (`QA_AUTH_RATE_BUDGET`, `utils/auth_rate_guard.py`).
None of these is active unless you set it. Required secrets, variables, runner setup and the
artifact redaction are in `docs/qa/automation-ci.md`.

## Allure report

Allure results are written to `reports/allure-results` on every run (`pytest.ini`'s
`--alluredir`). To turn that into the HTML report, you need the Allure commandline tool, which
needs a JRE. Two options:

**Option A — portable, no admin rights (what this session used):**

```powershell
# one-time download, ~80 MB total, extracted under qa/automation/.tools/ (git-ignored)
Invoke-WebRequest "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse?project=jdk" -OutFile .tools\jre.zip
Expand-Archive .tools\jre.zip -DestinationPath .tools -Force
Remove-Item .tools\jre.zip

Invoke-WebRequest "https://github.com/allure-framework/allure2/releases/download/2.46.1/allure-2.46.1.zip" -OutFile .tools\allure.zip
Expand-Archive .tools\allure.zip -DestinationPath .tools -Force
Remove-Item .tools\allure.zip

# generate + open, via the wrapper (sets JAVA_HOME to the portable JRE for you)
.\run_allure.ps1 generate reports/allure-results -o reports/allure-report --clean
.\run_allure.ps1 open reports/allure-report
```

**Option B — system-wide (if you have/allow it):** `npm install -g allure-commandline` (or
`scoop install allure`), then use `allure` directly in place of `.\run_allure.ps1` in the commands
above.

A `winget install` of a JRE was attempted first in this session and **abandoned**: it silently
waited on a UAC elevation prompt that a non-interactive session can never answer. If you see a
stray elevated `msiexec` window from an earlier attempt, it's safe to cancel — Option A above
doesn't need it.

## Retries (transient failures only)

`pytest-rerunfailures` is installed but **not enabled by default** — `pytest.ini`'s `addopts`
does not include `--reruns`, so a bare `pytest` run never retries anything, and a real bug never
looks flaky by accident. To retry, pass `--reruns N` **together with** `--only-rerun <regex>` so
only failures whose exception text matches the pattern (connection/timeout-shaped errors) get a
second try; a plain `AssertionError` never matches and never reruns.

Verified in this session:
- A deliberate `assert False`-style failure (`TENANT-HOST-002`, a genuine `AssertionError`) ran
  **once** under `--reruns 2 --only-rerun "ConnectionError|ConnectTimeout|Timeout"` — 0 reruns.
- A deliberate `requests.exceptions.ConnectTimeout` (pointed at a closed port) was retried twice
  under the same flags before failing — confirmed via pytest's own `"1 failed, 2 rerun"` summary.

## Failure evidence

On any failing test, in addition to pytest-playwright's own trace/screenshot under
`test-results/`, the Allure result gets:

- **UI tests**: full-page screenshot, the URL at failure time, and any captured
  `console.error(...)` messages / uncaught page exceptions (`browser-console-errors` attachment;
  only added when there is something to show).
- **API tests**: a JSON log of every request/response made by that test's `ApiClient`
  (`api-request-response-log (redacted)`), including method, URL, headers, status, duration, and
  bodies — with `password`, `accessToken`, `refreshToken`, `otp`, etc. redacted before attaching
  (see `utils/redact.py`).

This is wired in `conftest.py`'s `pytest_runtest_makereport` hook plus the `_capture_console_errors`
fixture; the console-error fixture checks `request.fixturenames` for `"page"` before touching it,
so API-only tests never pay for a browser launch.

## Credential handling

- Configured via `QA_A_ADMIN_USERNAME` / `QA_A_ADMIN_PASSWORD` (env or `.env`), read through
  `data.test_data.admin_user_a()`. `QA_A_ADMIN_IDENTIFIER` (the Phase 1 name) is still accepted as
  a fallback for the username so an existing `.env` keeps working.
  A missing variable **skips** the test (`pytest.skip`, via `utils.env_utils.require_env[_any]`)
  — it is never guessed, defaulted, or silently treated as a pass.
- `TenantUser` (the credential holder) has a custom `__repr__`/`__str__` that always prints
  `password='***redacted***'`, so an accidental `print(user)`, pytest failure repr, or log line
  can't leak it.
- `core/api_client.py`'s request/response history — the thing that gets attached to Allure on
  failure — is passed through `utils.redact.redact_json` / `redact_text` first: `password`,
  `accessToken`, `refreshToken`, `token`, `secret`, `otp` keys are masked, and the `Authorization`
  header is reduced to its scheme (`Bearer ***redacted***`).
- `environment.properties` records only `Credentials.AdminConfigured=True/False`,
  `Credentials.EmployeeRoleConfigured=True/False`, and `Credentials.ManagerRoleConfigured=True/False`
  — never the values.

## Test data and cleanup (Employee module)

- Every employee this suite creates has its last name prefixed `QAAUTO` (`data.test_data.TEST_DATA_MARKER`
  / `unique_last_name()`), with a random suffix so parallel runs (`-n auto`) never collide.
- The `created_employee` fixture (`conftest.py`) creates one disposable employee via the API
  before a test that needs an existing record, and deletes it (`DELETE /api/employees/{id}`,
  `Employee.Delete`) in teardown, logging a warning — never failing the test — if that delete
  itself fails. `test_duplicate_employee_code_is_rejected` and `test_reporting_manager_display`
  create a second employee directly and clean it up the same way in a `try`/`finally`.
  `test_create_employee_minimum_valid_data` (UI) creates through the real form, then looks its own
  record up by its unique last name via the API to get an id to delete.
- **No pre-existing employee is ever read for its values, edited, or deleted.** Every id used in
  an assertion or a write comes from this run's own setup response — never a hard-coded GUID and
  never one discovered by browsing the tenant's existing directory.
- If a run is killed mid-test (Ctrl-C, crash) a `QAAUTO*`-prefixed record can be left behind;
  search the directory for `QAAUTO` and delete by hand, or re-run — codes/names are always fresh
  so a leftover never collides with (or blocks) a later run.

## Test data and cleanup (Leave module)

- **No Leave Type/Policy/Period configuration is ever created, modified, or published by this
  suite.** Leave is a deliberately restricted MVP (`qa/07-leave/README.md`, CR-81): a published
  policy with a half-day/sandwich/attachment/clubbing rule is silently unusable at real request
  time, so this batch never risks breaking a tenant's existing configuration by writing new policy
  rows — it only reads what's already there.
- `test_employee_cannot_manage_leave_types` sends a `POST /api/leave-types` as a caller who lacks
  `Leave.TypeManage` — the request is expected to be rejected with 403 before any row is created, so
  there is nothing to clean up; if this ever unexpectedly returns 201 in some environment, that is
  itself a defect finding, not a cleanup gap.
- `test_preview_has_no_side_effects` and `test_submit_and_withdraw_pending_leave_request` are the
  only tests that touch request data, and only as the `QA_A_EMPLOYEE` identity's **own** requests —
  they discover whether a submittable Leave Type currently exists via the self-service `available`
  list plus a real `POST /api/leave-requests/preview` call (see `_find_previewable_leave_type` in
  `tests/api/test_leave_smoke.py`), and **skip honestly**, with the response body attached to
  Allure, when none does. Any request the second test submits is withdrawn
  (`POST /api/leave-requests/{id}/withdraw`) before the test returns — `Withdrawn` is a terminal
  status, so no further cleanup step exists or is needed. No pre-existing leave request belonging to
  any other employee is ever read, approved/rejected, or withdrawn.
- `test_manager_approval_inbox_is_accessible` and `test_my_leave_balance_is_readable` are read-only.

## Environment/tenant model

The backend resolves a tenant from the **Host header** of the request
(`TenantShardResolutionMiddleware`, per `CLAUDE.md`), not from anything the client asserts. So:

- **UI tests** navigate the real browser to `http://<tenant-host>:<QA_UI_PORT>/...` — the tenant
  host must actually resolve (Windows resolves `*.localhost` to loopback out of the box).
- **API tests** always connect to `localhost:<QA_API_PORT>` and set the `Host` header explicitly
  to the tenant host under test (`core/api_client.py`) — exactly what a real request arriving
  through a reverse proxy looks like.

`QA_UNKNOWN_HOST` (default `qa-unregistered-workspace.localhost`) is a host that resolves to no
organization, used for the tenant-host-resolution negative tests.

## Tests (Authentication smoke suite)

Traced to `qa/01-authentication/cases/*.yaml`:

| Test | Layer | Manual case | Needs credentials? |
|---|---|---|---|
| `test_login_page_loads` | UI | AUTH-LOGIN (page load) | No |
| `test_valid_login_succeeds` | UI | AUTH-LOGIN-001 | Yes |
| `test_invalid_credentials_are_rejected` | UI | AUTH-LOGIN (negative) | No |
| `test_unknown_tenant_host` | UI | TENANT-HOST-002 | No |
| `test_logout_redirects_to_login` | UI | AUTH-REFRESH-016 | Yes |
| `test_protected_page_redirects_unauthenticated_user` | UI | RequireAuth guard | No |
| `test_valid_login_returns_tokens` | API | AUTH-LOGIN-001 | Yes |
| `test_invalid_credentials_return_401` | API | AUTH-LOGIN (negative) | No |
| `test_logout_then_refresh_is_rejected` | API | AUTH-REFRESH-016/017 | Yes |
| `test_protected_endpoint_requires_authentication` | API | `[Authorize]` convention | No |
| `test_login_at_unknown_host_is_refused` | API | TENANT-HOST-002 | No |

11 tests total (6 UI, 5 API), all marked `smoke` + `regression`, the business-critical ones also
`critical`.

## Tests (Employee smoke suite, Phase 3 — first batch)

Traced to `qa/05-employee/cases/*.yaml`. Deliberately narrow: one flow each of what the brief
asked for, not full Module 05 coverage (see `qa/05-employee/README.md` for the ~170-functionality
scope this is a slice of).

| Test | Layer | Manual case | Needs credentials |
|---|---|---|---|
| `test_employee_list_page_loads` | UI | list page load | Admin |
| `test_employee_search_filters_results` | UI | EMP-LIST-004 | Admin |
| `test_open_employee_details` | UI | detail view | Admin |
| `test_create_employee_minimum_valid_data` | UI | EMP-CREATE-005 / EMP-FE-002 | Admin |
| `test_create_employee_missing_required_fields_shows_validation` | UI | EMP-FE-004 / EMP-PD-001 | Admin |
| `test_edit_basic_employee_personal_details` | UI | Personal Details edit | Admin |
| `test_duplicate_employee_code_is_rejected` | API | EMP-CREATE-004 | Admin |
| `test_contact_and_address_save` | API | EMP-CA-001 | Admin |
| `test_bank_detail_crud_lifecycle` | API | EMP-BANK-001 / EMP-BANK-008 | Admin |
| `test_employment_details_round_trip` | API | EMP-JOIN-001 | Admin |
| `test_reporting_manager_display` | API | EMP-MGR-005 | Admin |
| `test_portal_account_visibility` | API | (portal-account, `User.View`-gated) | Admin |
| `test_authorization_denial_for_unauthorized_user` | API | EMP-SEC-002 | Admin + Employee-role |

13 tests total (6 UI, 7 API), all marked `smoke` + `regression`, the business-critical ones also
`critical`. New framework pieces: `core/employee_api.py` (endpoint wrappers),
`pages/employees_list_page.py` + `pages/employee_form_page.py` (Page Objects for the directory and
the shared create/edit/detail Personal Details form), `data.test_data.employee_user_a()` +
unique-data generators, and three `conftest.py` fixtures — `admin_api_client`, `created_employee`,
`ui_signed_in` (the last logs the browser in with "Remember Me" checked so a test can `page.goto()`
any protected route directly afterward, since a full navigation drops the in-memory access token).

**Deliberately not covered this batch** (would need more setup or master data than "first batch,
focused" allows): the legacy full-record create path's organizational fields (department/
designation/manager — needs master-data ids), L1 manager resolution via Employment History (the
free L2 peer field used instead, see EMP-MGR-005), family/education/previous-employment/documents/
audit-log sub-resources, cross-tenant scenarios (needs Tenant B), and the frontend's
department/designation reference filters (needs seeded master data to be meaningful).

## Tests (Leave smoke suite, Phase 4 — first batch)

Traced to `qa/07-leave/cases/*.yaml`. Deliberately narrow — see `qa/07-leave/README.md` for the
319-functionality/325-case scope this is a slice of, and its CR-77 through CR-90 findings (in
particular CR-81: a published policy with half-day/sandwich/attachment/clubbing rules is silently
unusable at real request time, which is why the two request-lifecycle tests below discover
eligibility live rather than assuming any policy in the tenant is exercisable).

| Test | Layer | Manual case | Needs credentials |
|---|---|---|---|
| `test_leave_types_list_is_readable` | API | LEAVE-TYPE-001 | Admin |
| `test_available_leave_types_lists_only_active` | API | LEAVE-OPT-001 | Employee |
| `test_my_leave_balance_is_readable` | API | LEAVE-BAL-001 (partial) | Employee |
| `test_my_leave_requests_list_is_self_scoped` | API | LEAVE-AUTHZ-005 | Employee |
| `test_preview_has_no_side_effects` | API | LEAVE-REQ-001 | Employee |
| `test_submit_and_withdraw_pending_leave_request` | API | LEAVE-REQ / LEAVE-CANCEL-001 | Employee |
| `test_manager_approval_inbox_is_accessible` | API | LEAVE-INBOX-001 (partial) | Manager |
| `test_unauthenticated_leave_requests_call_is_rejected` | API | LEAVE-AUTHZ-002 | No |
| `test_employee_cannot_manage_leave_types` | API | LEAVE-AUTHZ-003 | Employee |
| `test_my_leave_requests_page_loads` | UI | LEAVE-FE-001 (partial) | Employee |
| `test_employee_direct_navigation_to_leave_policies_is_forbidden` | UI | LEAVE-FE-013 | Employee |

11 tests total (2 UI, 9 API), all marked `smoke` + `regression`, the business-critical ones also
`critical`. New framework pieces: `core/leave_api.py` (endpoint wrappers for Leave Types, the
self-service Request Options/Requests/Balances surface, and Approvals),
`pages/my_leave_requests_page.py` (Page Object for the self-service list),
`data.test_data.manager_user_a()`, and three `conftest.py` fixtures — `employee_api_client`,
`manager_api_client`, `ui_signed_in_employee` (the API/UI equivalents of `admin_api_client`/
`ui_signed_in`, but signed in as a **linked** Employee/Manager rather than the admin seed user,
since Leave's self-service endpoints resolve the caller's own linked Employee identity and a
TenantAdmin seed user is not necessarily one).

"(partial)" above means the case's full scenario needs data this batch doesn't set up (e.g.
LEAVE-BAL-001's Allocated/Unlimited/NoBalanceRequired three-way distinction, LEAVE-INBOX-001's
"exactly this manager's direct reports, never an unrelated employee's" scoping proof) — the
automated test exercises the same endpoint/permission but only asserts the shape/status that's
verifiable without provisioning that data.

**Deliberately not covered this batch**: any Leave Type/Policy/Period/Working-Day-Calendar CRUD or
publish flow (LEAVE-TYPE-002…011, all of `Leave_TypesPolicies`/`Leave_EffectiveDates`), Balance
Import (`Leave_Balance`'s Import cases), Comp-Off (its own controller family, out of scope for this
Leave-only batch), the Team Leave Calendar and HR Leave Dashboard pages, Leave Reports, the Reminder
Worker, approve/reject (only the inbox read + owner-withdraw lifecycle actions are exercised —
approving/rejecting an actual pending request would need a real manager↔employee reporting-line
setup this batch doesn't provision), and every concurrency/provider-parity case (`Leave_Concurrency`
— SQLite-only local runs prove nothing about the MySQL/SQL Server locking this module depends on,
per `qa/07-leave/README.md`).

## Tests (Attendance smoke suite, Phase 5 — first batch)

Traced to `qa/08-attendance/attendance-test-cases.generated.csv` (sheets Attendance_Self,
Attendance_Regularization, Attendance_OnDuty, Attendance_Reports, Attendance_Security,
Attendance_Frontend). Deliberately narrow — see `qa/08-attendance/README.md` for the 455-case, 17-
sheet scope this is a slice of, and its CR-91 through CR-128 findings, in particular CR-111/CR-112:
**no seeded role except SuperAdmin/TenantAdmin holds `Attendance.RegularizationRequest/Approve` or
`Attendance.OnDutyRequest/Approve`, and the seeded plain `Employee`/`Manager` roles do not hold
plain `Attendance.View` either** — reconfirmed directly against the current `SeedData.
RolePermissionMap` while building this batch, not merely cited from the manual catalogue. This is
why every functional (non-authorization) test below runs against `admin_api_client`/`ui_signed_in`
rather than `employee_api_client`/`manager_api_client`, and why the approve/reject tests are
submit-then-self-approve maker-checker denials (ATT-REG-014) rather than a two-identity happy path
— this environment has no second Attendance-capable identity configured.

| Test | Layer | Manual case | Needs credentials |
|---|---|---|---|
| `test_my_attendance_calendar_and_summary_is_readable` | API | ATT-SELF-001 | Admin |
| `test_my_attendance_day_detail_is_readable` | API | ATT-SELF-002 | Admin |
| `test_manager_team_is_readable` | API | ATT-SELF-012 | Admin |
| `test_regularization_submission_creates_pending_request` | API | ATT-REG-001 | Admin |
| `test_regularization_submission_without_reason_is_rejected` | API | ATT-REG (validation) | Admin |
| `test_employee_sees_regularization_status_after_cancel` | API | ATT-SELF (history) | Admin |
| `test_regularization_owner_cannot_approve_own_request` | API | ATT-REG-014 | Admin |
| `test_manager_regularization_queue_is_accessible` | API | ATT-REG (manager queue) | Admin |
| `test_on_duty_submission_creates_pending_request` | API | ATT-OD-001 | Admin |
| `test_on_duty_owner_cannot_approve_own_request` | API | ATT-OD-012 (maker-checker) | Admin |
| `test_manager_on_duty_queue_is_accessible` | API | ATT-OD (manager queue) | Admin |
| `test_daily_report_is_readable` | API | ATT-RPT-001 | Admin |
| `test_unauthenticated_attendance_call_is_rejected` | API | ATT-SEC-004 (partial) | No |
| `test_employee_cannot_access_manager_regularization_queue` | API | ATT-SEC | Employee |
| `test_employee_cannot_approve_regularization` | API | ATT-SEC | Employee |
| `test_manager_role_cannot_approve_under_seeded_permissions` | API | ATT-SEC (CR-112) | Manager |
| `test_tenant_isolation_on_attendance_calendar` | API | ATT-SEC-001 (skips — no Tenant B) | No |
| `test_employee_direct_navigation_to_my_attendance_is_forbidden` | UI | ATT-FE (route guard) | Employee |
| `test_employee_direct_navigation_to_attendance_requests_is_forbidden` | UI | ATT-FE (route guard) | Employee |
| `test_my_attendance_page_loads` | UI | ATT-SELF-001/FE | Admin |
| `test_attendance_requests_page_loads` | UI | ATT-REG-001/ATT-OD-001/FE | Admin |

20 tests total (4 UI, 16 API), all marked `smoke` + `regression`, the business-critical ones also
`critical`. New framework pieces: `core/attendance_api.py` (endpoint wrappers for the self-service
calendar/day read, manager team read, Regularization/On Duty submit-cancel-approve-reject, and the
Daily report), `pages/my_attendance_page.py` + `pages/attendance_requests_page.py` (Page Objects for
`/attendance/my-attendance` and `/attendance/requests`), and `data.test_data.unique_reason()`. No
new `conftest.py` fixtures were needed — `admin_api_client`/`employee_api_client`/
`manager_api_client`/`ui_signed_in`/`ui_signed_in_employee` already covered every identity this batch
needs.

"(partial)" above means the case's full scenario needs more than this batch sets up (e.g.
ATT-SEC-004's full representative sweep covers 8 controllers; this batch exercises one representative
self-service endpoint rather than repeating the same 401 assertion 8 times).

**Deliberately not covered this batch**: Foundation/Configuration (Shift/Pattern/Applicability/
Roster/Calendar Overrides — `Attendance_Config`, 79 cases), Punch/Event Ingestion (`Attendance_
Events`), Daily Derivation internals (`Attendance_Daily`), Monthly Processing/period lock/payroll
snapshot (`Attendance_Monthly`), the Operations exception workbench (`Attendance_Exceptions`),
Overtime (`Attendance_Overtime` — no seeded role holds any Overtime permission at all per CR-114, and
CR-105 records no team/self view endpoint exists), Admin Corrections (`Attendance_Admin`), the full
Scope/authorization-matrix deep dive (`Attendance_Scope`), monthly/exceptions reports and the export
endpoints (`Attendance_Reports` beyond Daily), Devices/Ingestion (`Attendance_Devices`, 43 cases),
Leave/Calendar Integration (`Attendance_Integration`), Concurrency/Provider-parity (`Attendance_
Concurrency` — SQLite-only local runs prove nothing about the MySQL/SQL Server locking this module
depends on, per `qa/08-attendance/README.md`), and the remaining `Attendance_Security`/`Attendance_
Frontend` cases beyond the ones listed above (including the confirmed CR-125/CR-126/CR-128 frontend
defects, which are read-only findings in the manual catalogue, not yet automated).

## Test data and cleanup (Attendance module)

- **No Foundation/Shift/Pattern/Applicability/Roster configuration is ever created, modified, or
  published by this suite.** It only reads/writes through the self-service Regularization/On Duty
  workflow, as the configured `QA_A_ADMIN` identity's **own** requests.
- Every Regularization/On Duty request this suite submits carries a reason built from
  `data.test_data.unique_reason()` (prefixed `QAAUTO`, same marker as Employee/Leave), and is
  cancelled via the self-service cancel endpoint (`POST me/regularizations/{id}/cancel` /
  `POST me/on-duty/{id}/cancel`) before the test returns — `Cancelled` is a terminal status, so no
  further cleanup step exists or is needed. `test_regularization_owner_cannot_approve_own_request`
  and `test_on_duty_owner_cannot_approve_own_request` cancel their created request in a `finally`
  block regardless of whether the maker-checker assertion passed.
- **No pre-existing employee's attendance record, Regularization, or On Duty request is ever read
  for its values, edited, or deleted.** Every id used in an assertion or a write comes from this
  run's own setup response.
- Regularization submissions use `businessDate` values in the past (yesterday and earlier) because
  `AttendanceWorkflowService.SubmitRegularizationAsync` rejects a future date outright; On Duty
  submissions use future dates specifically to avoid colliding with any existing attendance day for
  the identity under test. Neither choice mutates `EmployeeAttendanceDay` itself — only an
  `AttendanceRegularizationRequest`/`AttendanceOnDutyRequest` row is created, and only while
  `Pending`, before this suite cancels it.
- If a run is killed mid-test (Ctrl-C, crash), a `QAAUTO`-marked Regularization/On Duty request can
  be left `Pending`; it is safe to leave (it blocks nothing else this suite does, since business
  dates are chosen to be unique per test run via `unique_reason()`'s random suffix only in the
  *reason* text, not the date — a second run reusing the same relative date will get a `409 A
  pending regularization already exists for this date` and should be treated as a stale leftover to
  cancel by hand, not a framework defect).

## Results from this session (Phase 2)

Run against the local dev stack (`dotnet run --project Backend/HRMS.API`,
`npm run dev` in `Frontend/HRMS.Web`), tenant `ANEVRA01` (host `anevra01.localhost`),
`--browser chromium`, no `QA_A_ADMIN_*` configured:

- **6 passed**, **4 skipped**, **1 failed** — identical outcome to Phase 1, now with a real Allure
  HTML report behind it (`reports/allure-report/widgets/summary.json`:
  `{"failed": 1, "broken": 0, "skipped": 4, "passed": 6, "unknown": 0, "total": 11}`).
- **4 skipped** are still the two valid-login and two logout tests —
  `QA_A_ADMIN_USERNAME`/`QA_A_ADMIN_PASSWORD` remain unset. See "Credentials status" below.
- **1 failed — TENANT-HOST-002, kept failing, not masked.** `POST /api/auth/login` at an
  unregistered host returns **HTTP 500** (`"Connection string 'SqlServer' is not configured."`)
  instead of the `401` the manual case expects. Unchanged from Phase 1; explicitly re-verified
  this session with the same assertion (`response.status_code == 401`) and with retries enabled
  (`--only-rerun` correctly did not retry it, since it's an `AssertionError`, not a transient
  error).

## Results from this session (Phase 3)

Same local dev stack, same tenant, `--browser chromium`, still no `QA_A_ADMIN_*`/`QA_A_EMPLOYEE_*`
configured — combined with Phase 1/2's suite this run was **24 tests total: 6 passed, 1 failed,
17 skipped** (`reports/allure-report/widgets/summary.json`:
`{"failed": 1, "broken": 0, "skipped": 17, "passed": 6, "unknown": 0, "total": 24}`).

- The 6 passed / 1 failed are unchanged from Phase 2 (Authentication) — Phase 3 added zero new
  passes/failures **in this environment** because every one of the 13 new Employee tests skips
  honestly on the same missing-credentials reason as before, not because anything is broken.
- Collection, marker filtering, parallel execution (`-n 2`, isolated — no shared test-data
  collisions), and Firefox/WebKit collection (24 tests under each, browsers not installed) were
  all re-verified with the Employee suite in place.
- TENANT-HOST-002 is still the one real failure, still not masked (see Phase 2 above).

## Results from this session (Phase 5)

Run against the local dev stack (`dotnet run --project Backend/HRMS.API`, `npm run dev` in
`Frontend/HRMS.Web`, both started fresh this session), tenant `ANEVRA01` (host
`anevra01.localhost`), `--browser chromium`, still no `QA_A_ADMIN_*`/`QA_A_EMPLOYEE_*`/
`QA_A_MANAGER_*` configured:

- **Attendance suite alone (all 21 tests, not just `smoke`-marked): 1 passed, 20 skipped, 0
  failed.** The 1 pass is `test_unauthenticated_attendance_call_is_rejected` (needs no
  credentials). Every other test skips honestly and specifically — 15 on the missing
  `QA_A_ADMIN_*`/`QA_A_EMPLOYEE_*`/`QA_A_MANAGER_*` pairs, 1 (`test_tenant_isolation_on_
  attendance_calendar`) on the absent `QA_TENANT_B_HOST` — never a fabricated pass or a
  misleading failure.
- **Full combined suite (Authentication + Employee + Leave + Attendance, all markers): 56 tests
  total — 1 failed, 8 passed, 47 skipped.** The 1 failure is the same pre-existing TENANT-HOST-002
  finding from Phase 2/3 (`POST /api/auth/login` at an unregistered host returns 500, not the 401
  the manual case expects) — re-verified this session, unrelated to Attendance, not masked.
- Collection is clean (no import/fixture errors) for all 21 new Attendance tests; Firefox/WebKit
  `--collect-only` both report **56 tests** (up from Phase 3's 24), confirmed working without
  either browser's binaries installed.
- **No new failures were introduced by this batch.** Everything Attendance-specific that didn't run
  is an honest, cited skip (missing credentials or missing Tenant B) or a documented business-rule
  gate (see "Defects/findings" implied throughout the test titles above, e.g. CR-111/CR-112), not
  a broken test or a masked defect.
- Because every credentialed test skipped, the maker-checker (ATT-REG-014-style), validation, and
  report-read assertions this batch adds are **unexercised in this environment** — they are
  verified only by code reading (the controller/service source cited in each test's docstring), not
  by a live pass. Re-run once `QA_A_ADMIN_USERNAME`/`QA_A_ADMIN_PASSWORD` point at a SuperAdmin/
  TenantAdmin seed user (the only seeded roles holding Attendance's Regularization/On Duty
  permissions — see below) to convert these into real pass/fail evidence.

## Phase 6 — dedicated QA Admin/Employee/Manager identities (ANEVRA01)

Closes blocker #7 below. Three DEV/TEST-only accounts now exist in `ANEVRA01`, provisioned by a
new, explicitly opt-in seeding path — **not** the always-on demo seeding, and **not** a manual
database edit:

- `Backend/HRMS.Infrastructure/Persistence/Seed/DatabaseSeeder.cs`'s `SeedQaAutomationUsersAsync`
  runs from the same `SeedShardAsync` every tenant already goes through on startup, but only does
  anything when **all** of these hold: the host environment is Development, the tenant's code is
  exactly `ANEVRA01` (case-insensitive), and `DevelopmentSeed:EnableQaAutomationUsers` plus all
  three of `DevelopmentSeed:QaAdminPassword` / `QaEmployeePassword` / `QaManagerPassword` are set in
  configuration (env vars or `dotnet user-secrets` against `Backend/HRMS.API` — never a literal in
  source). Any other tenant, any other environment, or a checkout with those unset: no-op, exactly
  as before. This mirrors the pre-existing `SeedDevelopmentRoleManagementVerificationAsync` pattern
  in the same file (also `ANEVRA01`-only, also Development-only, also config-gated) rather than
  inventing a second mechanism.
- **Idempotent / safe to re-run**: every insert is a "does this natural key already exist" check
  first (`Email` for Users, `EmployeeCode` for Employees, `UserId`/`EmployeeId` for the account
  link, the effective/unsuperseded row for the employment-history reporting line). Nothing here is
  ever deleted or overwritten by a normal run; a password is only ever replaced when
  `DevelopmentSeed:ResetQaAutomationPasswords` is explicitly set alongside it. No pre-existing user,
  employee, or link — real or otherwise — is read, edited, or deleted by this path.
- **Existing roles only, no new grants**: QA Admin gets the existing `TenantAdmin` role (same
  pattern the demo tenants' `admin@demo01.com` already uses), QA Employee gets the existing
  `Employee` role, QA Manager gets `Manager` **and** `Employee` (a manager is also a self-service
  employee). No new role, no new permission, no permission added to an existing role.
- **Linking**: QA Employee's and QA Manager's Employee records are linked to their accounts via a
  direct `AccountEmployeeLinkEvent`/`AccountEmployeeCurrentLink` insert — the same technique the
  pre-existing Role Management verification seed above already uses, not the permission-checked
  `AccountEmployeeLinkService` (there is no authenticated HTTP actor during startup seeding to
  authorize it, and `AccountEmployeeLink.Manage` is deliberately not granted to `TenantAdmin`/
  `SuperAdmin` in this codebase — see `RolePermissionMap`'s explicit exclusion of
  `AccountEmployeeLink.*` for those two roles). QA Admin is deliberately **not** linked to an
  Employee record, matching the brief and the demo tenants' own admin seed users.
- **Reporting line**: QA Manager is recorded as QA Employee's manager in
  `EmployeeEmploymentHistory.ManagerId` (the field `ManagerRoleProvisioningService`,
  `LeaveAuthorizationService`, and `AttendanceAuthorizationService` actually read for team-scoping —
  confirmed by reading all three), not just the display-only `Employee.ReportingManagerId` (set too,
  for consistency with the Employee directory UI).

### Accounts created

| Identity | Email (login, TenantAdmin only) | Employee Code (login for Employee/Manager) | Role(s) | Linked Employee |
|---|---|---|---|---|
| QA Admin | `qaauto-admin@anevra01.qa-automation.invalid` | — (no Employee identity, by design) | `TenantAdmin` | none |
| QA Employee | `qaauto-employee@anevra01.qa-automation.invalid` (own address) | `QAAUTO-EMP` | `Employee` | `QAAUTO-EMP` (reports to QA Manager) |
| QA Manager | `qaauto-manager@anevra01.qa-automation.invalid` (own address) | `QAAUTO-MGR` | `Employee`, `Manager` | `QAAUTO-MGR` |

`ANEVRA01`'s branding is `loginIdentifierMode: EmployeeCodeOnly` (confirmed live via
`GET /api/tenants/current/branding`). `AuthService.LoginAsync` deliberately still allows a
privileged (`TenantAdmin`/`SuperAdmin`) account to sign in by email even in that mode — "Tenant
administrators are deliberately never locked out by EmployeeCodeOnly" — so QA Admin signs in with
its email; QA Employee/Manager (plain `Employee`/`Manager` roles, no such fallback) must sign in
with their Employee Code. `qa/automation/.env` (git-ignored) now has all six values populated
accordingly, and `.env.example`'s existing variable names already covered this (no new variables
were needed).

### Verified live (direct HTTP calls against `dotnet run --project Backend/HRMS.API`, `Host: anevra01.localhost`)

| Check | Result |
|---|---|
| QA Admin login (email) | **200** — `role: TenantAdmin` |
| QA Employee login (employee code) | **200** — `role: Employee` |
| QA Manager login (employee code) | **200** — `roles: [Employee, Manager]` |
| QA Employee identity resolution (`GET /api/me/profile`) | **200** — resolves to `QAAUTO-EMP` |
| QA Manager identity resolution (`GET /api/me/profile`) | **200** — resolves to `QAAUTO-MGR` |
| Leave self-service — available types (`GET /api/leave-types/available`) | **200** (5 active types) |
| Leave self-service — own balances (`GET /api/leave-balances/mine`) | **200** |
| Manager Leave approval inbox (`GET /api/leave-approvals`) | **200** |
| Attendance self-service — own monthly summary (`GET /api/attendance/my/monthly-summary`) | **200** |
| Attendance self-service — own Comp-Off balance (`GET /api/attendance/comp-off/balance`) | **200**, `employeeId` resolves correctly to `QAAUTO-EMP` |
| Attendance self-service — calendar/day (`GET /api/attendance/me/calendar`, `me/days/{date}`) | **403 — blocked, see CR below** |
| Manager team roster (`GET /api/attendance/manager/team`) | **403 — blocked, see CR below** |
| Manager Regularization queue (`GET /api/attendance/manager/regularizations`) | **403 — blocked, CR-111/CR-112 (unchanged)** |
| Manager On Duty queue (`GET /api/attendance/manager/on-duty`) | **403 — blocked, CR-111/CR-112 (unchanged)** |

### New authorization gap found this session (do not silently elevate — reporting, not fixing)

CR-111/CR-112 (`qa/08-attendance/README.md`) already documented that the seeded `Employee`/
`Manager` roles hold none of `Attendance.RegularizationRequest/Approve` or
`Attendance.OnDutyRequest/Approve`; both were reconfirmed live above. This session found the gap is
**broader** than that: `AttendanceReadController` gates *every* one of its routes — including the
self-service `me/calendar`/`me/days/{date}` and the manager `manager/team`/`manager/team/{id}/{date}`
reads — behind the single broad `Attendance.View` permission, which **neither** seeded `Employee`
nor `Manager` holds (only `SuperAdmin`/`TenantAdmin`/`HRAdmin`/`TimeManager` do). The narrower
permissions these two roles *are* seeded with — `Attendance.MonthlyViewSelf`/`MonthlyViewTeam`,
`ExceptionView`/`ExceptionViewSelf`, `CompOffViewSelf`/`ViewTeam`/`Approve` — gate a *different*
controller (`AttendanceMonthlyController`'s `my/monthly-summary`, `CompOffController`), which is why
those two checks above passed while the calendar/day/team-roster ones didn't. One seeded grant is
also simply dead code as of this reading: `Attendance.MonthlyViewTeam` (granted to `Manager`) is not
referenced by `[HasPermission(...)]` on any controller action in this codebase. Per this task's
instruction, none of this was patched — `Manager`/`Employee` were not given `Attendance.View` or any
other extra grant; the gap is reported here for a human decision.

### Cleanup / reset procedure

**To stop future seeding** (does not remove existing rows): unset
`DevelopmentSeed:EnableQaAutomationUsers`, or set it to `false`
(`dotnet user-secrets remove "DevelopmentSeed:EnableQaAutomationUsers" --project Backend/HRMS.API`,
or the equivalent env var).

**To rotate a password**: set `DevelopmentSeed:ResetQaAutomationPasswords=true` alongside a new
`DevelopmentSeed:Qa{Admin,Employee,Manager}Password`, restart the API once, then unset the reset
flag again (leaving it `true` is harmless — it just re-hashes the same password on every future
startup — but is unnecessary).

**To fully remove the three accounts** (every id below is a fixed, deterministic GUID this seeder
always uses for these rows — see `SeedQaAutomationUsersAsync` — so this script is exact and
repeatable): run against `ANEVRA01`'s shard database only, e.g. via
`dotnet ef database update`'s connection or any MySQL client pointed at the tenant's own shard
(**never** `hrms_catalog`):

```sql
DELETE FROM AccountEmployeeLinkEvents  WHERE Id IN ('aaaaaaaa-0000-4a00-9a00-000000000015','aaaaaaaa-0000-4a00-9a00-000000000025');
DELETE FROM AccountEmployeeCurrentLinks WHERE LinkId IN ('aaaaaaaa-0000-4a00-9a00-000000000015','aaaaaaaa-0000-4a00-9a00-000000000025');
DELETE FROM EmployeeEmploymentHistory  WHERE Id = 'aaaaaaaa-0000-4a00-9a00-000000000016';
DELETE FROM UserRoleAssignmentEvents   WHERE Id IN ('aaaaaaaa-0000-4a00-9a00-000000000003','aaaaaaaa-0000-4a00-9a00-000000000013','aaaaaaaa-0000-4a00-9a00-000000000023','aaaaaaaa-0000-4a00-9a00-000000000027');
DELETE FROM UserRoles                  WHERE Id IN ('aaaaaaaa-0000-4a00-9a00-000000000002','aaaaaaaa-0000-4a00-9a00-000000000012','aaaaaaaa-0000-4a00-9a00-000000000022','aaaaaaaa-0000-4a00-9a00-000000000026');
DELETE FROM Employees                  WHERE Id IN ('aaaaaaaa-0000-4a00-9a00-000000000014','aaaaaaaa-0000-4a00-9a00-000000000024');
DELETE FROM Users                      WHERE Id IN ('aaaaaaaa-0000-4a00-9a00-000000000001','aaaaaaaa-0000-4a00-9a00-000000000011','aaaaaaaa-0000-4a00-9a00-000000000021');
```

After running this, also clear the `DevelopmentSeed:*` secrets/env vars (above) so a later startup
doesn't simply re-create the same rows — which it otherwise will, by design (that's what makes it
idempotent).

## Tests (Payroll smoke suite, Phase 7 — first batch)

Traced to `qa/10-payroll/cases/*.yaml` (Payroll_SalaryComponents, Payroll_SalaryStructures,
Payroll_EmployeeSalary, Payroll_PeriodsRuns, Payroll_Calculation, Payroll_Inputs, Payroll_Scope),
plus one representative case each from `qa/15-bank-advice-salary-disbursement/cases/01-generation-
eligibility.yaml` (BANKADV-GEN) and `qa/17-payroll-analytics-reconciliation/cases/01-overview-
summary-control-totals.yaml` (PYA-OVW). Deliberately narrow — see `qa/10-payroll/README.md`'s own
"Scope decision" (Payroll is the largest single domain in the product: 24 design docs, 20
controllers, 30+ services, 90+ existing xUnit classes) and its CR-165 through CR-182 findings, in
particular **CR-165/CR-168**, reconfirmed live while building this batch: only `SuperAdmin`/
`TenantAdmin` (the configured QA Admin identity) holds any Payroll permission in this tenant at
all — `HRManager` holds none of the module's core permissions and `Manager` holds zero Payroll
permissions of any kind — so every functional test below runs against `admin_api_client`, and
`employee_api_client`/`manager_api_client` are used only for the authorization-denial checks, where
a 403 is exactly the expected, in-scope outcome.

| Test | Layer | Manual case | Needs credentials |
|---|---|---|---|
| `test_create_salary_component_minimum_valid_data` | API | PAY-SC-001 | Admin |
| `test_salary_components_list_supports_filters` | API | PAY-SC-002 | Admin |
| `test_missing_code_or_name_is_rejected` | API | PAY-SC-004 | Admin |
| `test_salary_component_activation_lifecycle_and_history` | API | PAY-SC (activate/deactivate/history) | Admin |
| `test_employee_cannot_manage_salary_components` | API | PAY-SCOPE | Employee |
| `test_create_salary_structure_with_component` | API | PAY-SS-001 | Admin |
| `test_salary_structure_history_is_readable` | API | PAY-SS (history) | Admin |
| `test_create_employee_salary_assignment_binds_ctc` | API | PAY-ESA-001 | Admin |
| `test_get_effective_assignment_for_employee` | API | PAY-ESA (GetEffective) | Admin |
| `test_employee_salary_assignment_by_employee_lists_assignment` | API | PAY-ESA (by-employee) | Admin |
| `test_manager_has_zero_payroll_permissions` | API | PAY-SCOPE (CR-168) | Manager |
| `test_create_payroll_period_and_transition_lifecycle` | API | PAY-PR-001/004/006 | Admin |
| `test_payroll_run_prepare_scopes_to_qaauto_employee_only` | API | PAY-PR/PAY-CALC (see finding below) | Admin |
| `test_employee_cannot_manage_payroll_runs` | API | PAY-SCOPE | Employee |
| `test_payroll_input_batch_create_validate_preview_cancel` | API | PAY-IN | Admin |
| `test_bank_advice_requires_approval_then_generates` | API | BANKADV-GEN (partial — see finding) | Admin |
| `test_payroll_analytics_overview_and_control_totals` | API | PYA-OVW | Admin |
| `test_unauthenticated_payroll_call_is_rejected` | API | PAY-SCOPE | No |

18 tests total (0 UI, 18 API — Payroll's brief asked for API-first with UI reserved for key
journeys, and this narrow first batch found no journey where a browser test would add proof an API
test doesn't already give; see "Deliberately not covered" below), all marked `smoke` + `regression`,
the business-critical ones also `critical`. New framework pieces: `core/payroll_api.py` (endpoint
wrappers for every in-scope controller — Salary Component/Structure/Employee Salary Assignment,
Periods, Runs/Calculation, Inputs, Bank Advice, GL Accounting, Analytics/Reconciliation, plus Loans
and Variable Pay wrappers kept for a future batch but not exercised by any test in this one). No new
`conftest.py` fixtures were needed — `admin_api_client`/`employee_api_client`/`manager_api_client`
already covered every identity this batch needs; five new fixtures local to
`tests/api/test_payroll_smoke.py` (`salary_component` -> `salary_structure` -> `payroll_employee` ->
`employee_salary_assignment`, plus independent `payroll_period` -> `calculated_payroll_run`) chain
the Salary Component -> Structure -> Employee -> Assignment -> Period -> Run dependency this
module's flows are built on.

**Data-safety design, verified live before writing a single test** (not assumed): a direct probe of
ANEVRA01 confirmed zero pre-existing Salary Components, Salary Structures, Employee Salary
Assignments, Payroll Periods, or Payroll Runs — Payroll was a completely clean slate. Every fixture
therefore creates only its own QAAUTO-prefixed master data and its own QAAUTO employee(s);
`PayrollRunService.PrepareAsync`'s eligibility query requires an Employee Salary Assignment to exist
at all, so a run built by this suite can never sweep a real (non-QAAUTO) employee into a calculation
— confirmed live via `test_payroll_run_prepare_scopes_to_qaauto_employee_only`, which asserts the
tenant's other employees (17+ by the end of this session) were all correctly excluded. Payroll
Periods use a randomized, deliberately out-of-band fiscal year (2071-2470, via
`_unique_period_dates()`) so a period created by this suite can never collide with a real future
payroll cycle.

**A real, source-verified environment/design finding constrains this batch's achievable depth as a
happy path** — reported here, not routed around: `PayrollCalculationEngine.CalculateEmployeeAsync`
unconditionally calls the Overtime snapshot resolver for every eligible employee, regardless of
whether the employee's Salary Structure has any Overtime/Attendance-driven component at all.
`OvertimeService.ResolveAsync` requires an `AttendancePeriod` row whose `StartDate`/`EndDate` match
the Payroll Period *exactly*, with `Status == Closed`. Attendance Foundation/Monthly-Finalization is
out of scope for this batch (and, per `qa/08-attendance/README.md`, largely unconfigured in this
environment already), so **no payroll run can currently reach `Calculated` in ANEVRA01, for any
period** — confirmed reproducible across every period date this session tried, not an artifact of
the out-of-band 2071+ years. This is why `test_payroll_run_prepare_scopes_to_qaauto_employee_only`
asserts the real, live outcome (a deterministic `CalculationFailed`/`OvertimeNotFinalized` error for
every eligible employee) instead of an idealized "clean calculation," and why the Bank Advice test
proves only what's reachable (generation is correctly rejected pre-approval; Approve itself is
correctly rejected since the run never reaches `Calculated`) and skips the true positive path with
that precise reason. This in turn means **GL/Accounting posting and a real, non-zero Analytics/
Bank-Advice result are not reachable in this environment today, for any Payroll batch** — worth a
human/product decision: either provision Attendance Foundation + a finalized Monthly period matching
whatever Payroll Period dates a future batch uses, or make the Overtime-resolution call in the
Calculation Engine conditional on the Salary Structure actually containing an Overtime/
AttendanceBased component (it currently is not).

**Test data and cleanup (Payroll module)**:
- Salary Components/Structures have no delete endpoint (only activate/deactivate) — every one this
  suite creates is deactivated in fixture teardown, best-effort, never deleted. A leftover Active
  one after a killed run is safe to deactivate by hand (QAAUTO-prefixed Code/Name) or ignore.
- Employee Salary Assignments likewise have no delete (only `{id}/activate` / `{id}/deactivate`) —
  deactivated in teardown, best-effort.
- **Payroll Periods and Runs have no delete/reset action at all** once a period leaves Draft or a
  run leaves Draft/Prepared. Every QAAUTO period/run this suite creates is a **permanent** artifact
  in the tenant — identifiable by its QAAUTO-prefixed Code/Name/Notes, harmless (isolated to its own
  out-of-band 2071+ dates and its own QAAUTO employee), but not cleanable via the API. A human with
  direct database access could remove them; this suite does not attempt to.
- **The QAAUTO employee(s) this suite creates cannot actually be deleted**, confirmed by reading
  `EmployeeService.DeleteAsync` directly: once an employee has an Employee Salary Assignment (and
  especially once swept into a Payroll Run's employee snapshot / a `PayrollCalculationError` row),
  the delete's `SaveChangesAsync` throws `DbUpdateException` (an FK constraint from the referencing
  payroll rows) and the service returns `409 Conflict — "This employee record is still referenced
  and cannot be deleted."` The `payroll_employee` fixture still attempts the delete in teardown
  (best-effort, exception swallowed, matching this framework's established cleanup philosophy) but
  it will reliably fail once a test has run — every QAAUTO Payroll employee is, in practice, a
  permanent tenant record from this point forward. This is real, current product behavior
  (Employee module's own `Employee.Delete` guarding referenced records), not a framework defect; it
  was not present as a concern for the Employee/Leave/Attendance batches because those never created
  a payroll-referencing row for their disposable employees.
- Payroll Input batches ARE cleanly cancellable (`Cancelled` is intentionally reachable from
  Draft/Validated/ValidationFailed/Approved/Posted) — this suite's one input-batch test leaves its
  batch `Cancelled`, a genuinely terminal, harmless state, matching the Leave/Attendance modules'
  precedent for a real cleanup action rather than a permanent leftover.
- **No pre-existing employee, Salary Component/Structure, Employee Salary Assignment, Payroll
  Period, or Payroll Run belonging to any other test run or any real user is ever read for its
  values, edited, or deleted** by this suite — every id used in an assertion or a write comes from
  this run's own setup response.

**Deliberately not covered this batch**: Reimbursements/Claims, Payroll Settlement (Retro/Final
Settlement — CR-181 is a **confirmed defect**: `PayrollRetroSettlementService.EvaluateRetroAsync`
hardcodes every correction to zero difference, so a retro case can never reach `Evaluated` through
ordinary API use), Recalc/Off-Cycle/Adjustments/Reversals beyond the one Recalculate call already
exercised, GL/Accounting posting (needs a full accounting configuration + GL account + mapping setup
this narrow batch does not provision, and is unreachable today regardless — see the finding above),
Statutory configuration (only the Calculation Engine's *integration point* is in `qa/10-payroll`'s
own declared scope, and even that could not be exercised this batch since no run ever calculates),
Loans (Module 11) and Variable Pay (Module 12) — both explicitly out of `qa/10-payroll/README.md`'s
own declared scope, candidates for a future Module 11-and-beyond batch — and the Payroll frontend
(`Payroll_Frontend`, 10 cases; this batch is API-first per its brief and found no journey needing a
browser test this first time through).

## Tests (Payroll happy-path E2E on the QAAUTO sandbox)

`tests/api/test_payroll_happy_path_e2e.py` runs one full payroll month through the public API, on
the reserved 1950–1959 QAAUTO sandbox (`sandbox/payroll_sandbox.py`, `docs/qa/payroll-sandbox-setup.md`).
It drives the sandbox tool rather than duplicating it: the same preflight guards (G1–G10), the same
per-write guard (`Api.write`: pinned host, and either a new `QAAUTO-HP` key or a manifest-owned id),
the same find-or-create `seed`, and the same manifest step format, so
`python -m sandbox.payroll_sandbox cycle` can resume a month the suite left incomplete.

```powershell
$env:QA_PAYROLL_SANDBOX = "1"; $env:QA_PAYROLL_SANDBOX_TENANT = "ANEVRA01"
pytest tests/api/test_payroll_happy_path_e2e.py tests/api/test_payroll_known_defects.py `
  --alluredir=reports/allure-results/payroll-happy-path-e2e
.\run_allure.ps1 generate reports/allure-results/payroll-happy-path-e2e -o reports/allure-report/payroll-happy-path-e2e --clean
```

- **Opt-in.** Without both env vars the E2E module skips; it never writes by accident. If a guard
  fails with the opt-in set, every stage errors and nothing is written.
- **Bank master prerequisite.** Stages 08–09 need the `QAAUTO-BANK` master, which only the
  Development-only seeder creates: start the API once with `DevelopmentSeed:EnableQaAutomationBank=true`
  (user-secrets or env var; see `docs/qa/payroll-sandbox-setup.md` §4.1). Without it, `seed` skips the
  salary bank account, preflight G10 warns, and stage 08 fails with that instruction. Stages 10–11
  still run.
- **Idempotent.** `seed` creates nothing on a re-run. Each session takes the next free sandbox
  month, because an Approved run can't be re-opened, so the window allows 120 sessions. Don't run
  the E2E and the CLI at the same time: they share the manifest.
- **Ordered.** Stages share state and run in file order. Run the module serially (or with
  `--dist loadgroup`). A stage whose prerequisite failed skips and names that stage.
- **Safe for real data.** A count+hash fingerprint of non-sandbox employees, payroll periods,
  runs, assignments, components, structures, attendance periods and bank advice is taken before the
  first write; stage 12 fails if it changed. Attachments carry only sandbox rows or aggregate
  counts, never a raw list with real employees' names.

| Stage | QA cases (partial = only the legs the sandbox can reach) |
|---|---|
| 01 sandbox ready (preflight, seed incl. salary bank account, assignment effective in-window, 404 today) | PAY-ESA-006 (partial) |
| 02 payroll period created, Draft | PAY-PR-001 |
| 03 attendance created → processed (1 employee, 0 blocking) → close preview → Closed | ATT-MONTHLY-001, -018; -004, -019 (partial) |
| 04 run created, prepared: eligible = sandbox employee, 36 Excluded "No effective salary assignment" | PAY-PR-012; PAY-PR-011 (partial) |
| 05 calculate: 1 calculated, 0 failed, gross = net = 30000.00; plain re-calculate is 409 | PAY-CALC-001; PAY-CALC-002 (partial) |
| 06 recalculate: version + 1, new result id, same amounts; reads return only the current result | PAY-CALC-024; PAY-CALC-004 (partial: calculation history has no API) |
| 07 bank advice/GL on a Calculated run are 409; approve; history row; recalculate on Approved is 409 | PAY-PR-017, PAY-PR-021, PAY-CALC-003, BANKADV-GEN-003, GLACC-GEN-003 (all partial) |
| 08 bank advice generated: exactly one current Salary account (masked in the employee API); Draft batch, number/reference formats; one payment **Valid/Ready** with holder, bank, masked account, IFSC and branch from the account; totals 1 / 30000.00; second generate 409; results unchanged | BANKADV-GEN-004, -008, -012, BANKADV-ACCT-001, -006, -007, -008, BANKADV-VAL-006; BANKADV-GEN-009, -010 (partial) |
| 09 bank advice lifecycle: export/approve on Draft 409; prepare → Prepared; validate/prepare on Prepared 409; approve → Approved; second approve 409; export → CSV (header, one row matching the payment, no full account number), batch and payment Exported; re-export identical and stateless; cancel on Exported 409; history `Exported, Approved, Prepared, Generated`; run still Approved | BANKADV-LIFE-001, -007, -011, BANKADV-EXP-001, -002, -003, -005, -006, BANKADV-CANCEL-001, BANKADV-AUDIT-001, -003 (all on one batch, so partial where a case needs several) |
| 10 GL journal: Generated, Debit = Credit = 30000.00, EXP debit + PAY credit, number format, GET by id, second generate 409, results unchanged | GLACC-GEN-011, -013, -016; GLACC-GEN-004, -009, -012, -014, GLACC-RPT-005 (partial) |
| 11 analytics overview, control totals (superseded version excluded), run summary; `bankAdviceTotal` = 30000.00 | PYA-OVW-001, -007; PYA-OVW-003, -004 (partial) |
| 12 real-data fingerprint unchanged | plan I4 (no QA case) |

Stage 09 stops at `Prepared` and skips with a B3 message if a persisted payroll control row blocks
self-approval (preflight G8). `Exported` is the last Bank Advice status; nothing after it
(bank submission, payment confirmation) exists in the product (BANKADV-EXP-008). The payroll run
is left `Approved`, not finalized.

**Known-defect regression checks** (`tests/api/test_payroll_known_defects.py`, read-only, marker
`known_defect`). Each asserts the correct contract, so it **fails while the defect exists**. Use
`-m "not known_defect"` only when you need a green gate and say so.

| Test | Defect | QA case |
|---|---|---|
| `test_gl_list_endpoint_returns_paged_result[gl-accounts / gl-configurations / gl-journals]` | F1: GL list endpoints return 500 (abstract `PagedQuery` bound from the query) | GLACC-ACCT-007, GLACC-CFG-001, GLACC-RPT-003 |
| `test_swagger_document_generates` | F2: `/swagger/v1/swagger.json` returns 500 (`MasterImportController.Validate`) | AUTH-SEC-002 (Development leg; no dedicated case) |
| `test_payroll_controls_report_matches_approval_enforcement` | F3: `GET /api/payroll/controls` reports maker-checker on with no persisted row, while a sandbox run was self-approved | PAY-PR-018, F-PAY-082 (nearest; no dedicated case) |

**Run of 2026-09-29:** 16 tests, 11 passed, 5 failed (F1 ×3, F2, F3), 0 skipped. Sandbox months
1950-02 and 1950-03 were used. The 1950-02 session failed stage 08 on a test bug (the API omits null
fields, `JsonIgnoreCondition.WhenWritingNull`), since fixed. Its manifest entry records
`stagesPassed` without `bank_advice`. The 1950-03 session passed all 11 stages.

**Bank Advice run of 2026-09-29 (12 stages):** the E2E module alone passed 12/12 on month 1950-04.
E2E plus known-defect checks: 17 tests, 12 passed, 5 failed (F1 ×3, F2, F3, unchanged), 0 skipped,
on month 1950-06. The CLI `cycle` ran 1950-05 through `bank_advice_exported` as well. New findings
F4 (export is a state-changing GET) and F5 (IFSC unmasked in Bank Advice) are in the sandbox doc §8;
they have no regression checks yet.

## Tests (GL lifecycle + Payroll Analytics E2E on the QAAUTO sandbox)

`tests/api/test_gl_analytics_e2e.py` takes one sandbox month through the whole GL journal lifecycle
and then reads Payroll Analytics for it. It reuses the sandbox tool the same way the happy-path E2E
does (preflight G1–G10, `Api.write`, find-or-create `seed`, manifest steps). The month itself comes
from `sb.run_cycle`, so this module doesn't depend on the happy-path module having run.

```powershell
$env:QA_PAYROLL_SANDBOX = "1"; $env:QA_PAYROLL_SANDBOX_TENANT = "ANEVRA01"
pytest tests/api/test_gl_analytics_e2e.py tests/api/test_gl_analytics_known_defects.py `
  --alluredir=reports/allure-results/gl-analytics-e2e
.\run_allure.ps1 generate reports/allure-results/gl-analytics-e2e -o reports/allure-report/gl-analytics-e2e --clean
```

- **Which journal moves.** Only the journal of the month this session completed. Months 1950-01 to
  1950-06 keep their `Generated` journals as evidence and are never transitioned.
- **Idempotent.** The cycle records `glLifecycle` (`origin`, `state`) and one step per transition
  (`gl_journal_validated`, `…_approved`, `…_exported_approved`, `…_posted`, `…_exported_posted`,
  `reconciliation_pre`, `reconciliation_post`). A rerun resumes an `open` lifecycle only while its
  journal is still `Generated`. Otherwise it marks it `abandoned:<status>` and completes a new month.
  No journal is transitioned twice.
- **One extra write outside the session's month.** Stage 13 needs a run whose bank advice total
  differs from net pay: the earliest such sandbox month (1950-01, whose batch predates the salary
  account and totals 0.00). It posts one reconciliation there, records its id in that cycle's
  `analyticsEvidence`, and on every rerun only reads it back.
- **Export is a read.** GL export is a stateless GET (CR-276), so it goes through the plain client.
- **Isolation.** The same non-sandbox fingerprint as the happy-path E2E, checked in stage 14.

| Stage | QA cases (partial = only the legs the sandbox can reach) |
|---|---|
| 01 month ready: seed creates nothing, run Approved, bank advice Exported, journal Generated | GLACC-GEN-003 (partial) |
| 02 journal Generated (never Draft); Debit = Credit = 30000.00; one debit per current component and one net-payable credit, traced by `sourceId` to the current result (not the superseded one); number/date/currency; second generate, export, approve and post are 409 | GLACC-GEN-004, -009, -011, -012, -013, -014, -005 (partial), GLACC-REV-006, GLACC-EXP-001, GLACC-LC-003, -012, GLACC-RPT-005 |
| 03 validate → Validated ("Payroll journal validated."), totals/lines unchanged, response equals a fresh read; repeat validate, post and export are 409 | GLACC-LC-001, -002, -008, GLACC-CONC-005, GLACC-EXP-001, GLACC-LC-011 (partial) |
| 04 approve → Approved ("Payroll journal approved."); repeat approve and backward validate are 409 | GLACC-LC-012; GLACC-LC-006, -011 (partial: `ApprovedAtUtc` and `ConcurrencyVersion` are not in the DTO) |
| 05 export while Approved: 200 `text/csv; charset=utf-8`, `payroll-journal-{number}.csv`, exact header, one row per line in sequence, totals; status still Approved | GLACC-EXP-001, -002, -005, GLACC-REV-005 (partial) |
| 06 post → Posted ("Payroll journal posted within HRMS."); post, validate and approve on Posted are 409 | GLACC-LC-008, -009, -012, -011 (partial) |
| 07 export while Posted ×3: byte-identical to the Approved export; status stays Posted | GLACC-EXP-004, GLACC-REV-004, -005 |
| 08 regeneration after post is 409; no `cancel`/`reverse` route (random-id probe); no history field, no `/history` route | GLACC-REV-001, -002, GLACC-HIST-003 |
| 09 overview, control totals, run summary and their CSVs equal payroll results, bank advice (batch and payments) and the Posted journal | PYA-OVW-001, -003, -004, -007, -011, GLACC-REC-006 |
| 10 departments and cost-centers: one `UNCLASSIFIED` row that sums to the run | PYA-DIM-004, -006, -007, -008 |
| 11 `variance.csv` with `compareRunId` = previous sandbox month (delta 0, NormalComparable) and without it (latest-ending other run, `PR-2424-04-001`, so NewValue); component drill-down answers | PYA-VAR-001, -002, -011, -013, PYA-CVAR-004 |
| 12 pre and post reconciliation of the clean run: version 1, Generated, no findings, all checks passed; GET by id; empty `findings.csv`; overview 0 open/critical/anomalies | PYA-RECGEN-001, -012, PYA-BANK-005, PYA-GL-007, PYA-VER-001, PYA-OVW-005, -006 (partial) |
| 13 post reconciliation of 1950-01: one Critical `BankAdviceAmountMismatch` (expected 30000.00, actual 0.00), no count or GL finding; overview 1 open/1 critical, 0 anomalies; `findings.csv` row | PYA-BANK-002, PYA-GL-007, PYA-OVW-005, PYA-VER-001, PYA-CSV-001, PYA-EXC-008 |
| 14 real-data fingerprint unchanged | plan I4 (no QA case) |

Stages that pin behavior the risk register calls a defect carry an Allure `cr` label and a
`CR-KNOWN-…` tag (`utils/allure_evidence.cr_refs`): CR-275, CR-276, CR-278, CR-279, CR-280, CR-298,
CR-302. Not reachable with one sandbox employee and no statutory data: AggregationMode and
employer-contribution mapping (CR-283), TaxTotal (CR-303), variance flags (CR-299), the findings
lifecycle, anomaly flags (only negative or zero net pay creates one), and self-approval blocking
(GLACC-LC-005 needs a persisted control row).

**Known-defect checks** (`tests/api/test_gl_analytics_known_defects.py`, read-only, marker
`known_defect`, each fails while its defect exists):

| Test | Defect | QA case |
|---|---|---|
| `test_journal_cancel_or_reverse_endpoint_exists[cancel / reverse]` | CR-275: no cancel/reverse API | GLACC-REV-001 |
| `test_exported_journal_reaches_exported_status` | CR-276: `Exported` unreachable; export is stateless | GLACC-REV-004 |
| `test_journal_history_is_exposed` | CR-278: history written, never returned | GLACC-HIST-003 |
| `test_analytics_csv_unknown_run_is_not_found[variance / findings / summary / control-totals]` | CR-305: CSV exports answer 400 where JSON answers 404 | PYA-CSV-007 |
| `test_analytics_list_endpoint_answers[variance / findings / exceptions / exceptions-csv / analytics-controls]` | F1: abstract `PagedQuery` binding, 500 | PYA-VAR-008, PYA-FIND-002, PYA-EXC-001, PYA-EXC-007, PYA-CTRL-001 |
| `test_component_variance_counts_current_results_only` | F6 (new): component variance sums superseded results | PYA-CVAR-001 |
| `test_component_variance_matches_comparison_run` | F7 (new): component variance BaseAmount is always 0 | PYA-CVAR-001 |

**Run of 2026-09-29 (final session, month 1950-10):** E2E, known-defect checks and the Employee
bank-detail smoke together: 30 tests, 15 passed (14 E2E stages + `test_bank_detail_crud_lifecycle`),
15 failed (all `known_defect`, each with its expected message), 0 skipped. Earlier sessions: 1950-07
(two test bugs, the reconciliation DTO key and a fixture scope, both fixed; its lifecycle completed,
its post reconciliation was not generated) and 1950-09 (13/14; stage 12 compared a 7-digit POST
timestamp with MySQL's 6-digit one, fixed). 1950-08 passed 14/14. Every session left the real-data
fingerprint unchanged and recorded no unexpected write.

## Credentials status

Configured as of Phase 6 (see above) for all three identities. `qa/HRMS_Test_Cases.xlsx`'s
`login*.json`/`token*.txt` files in the repo root are pre-existing scratch artifacts per
`CLAUDE.md`'s repo-hygiene note and were **not** read or used as a credential source.

## Remaining setup / blockers

1. ~~No `QA_A_ADMIN_USERNAME`/`QA_A_ADMIN_PASSWORD`/`QA_A_EMPLOYEE_*`/`QA_A_MANAGER_*`~~ **Resolved
   in Phase 6** — see "Phase 6" above. `qa/automation/.env` is populated; a plain `pytest -m smoke`
   run now exercises real credentials. Note found while doing this: a single dense run of the full
   smoke suite (many logins across Authentication+Employee+Leave+Attendance in a short window) can
   trip the backend's own `RateLimiting:Authentication` policy (20 requests/60s per
   `appsettings.json`, shared across all identities from one IP) and turn otherwise-passing tests
   into `429`s — this is the rate limiter working as designed, not a credential or seeding problem;
   re-running after the 60s window resets, or splitting the run into smaller batches, avoids it. Do
   not raise this limit to make CI green — it is a production security control.
2. **TENANT-HOST-002 environment finding** — unchanged, still a real product/environment behavior
   left for a human decision.
3. **Firefox/WebKit browsers not installed** (`playwright install firefox webkit`) — collection
   under both is verified working; only the binaries are missing locally.
4. **Allure CLI is portable, not system-installed** — works via `run_allure.ps1` +
   `.tools/`, but that directory is git-ignored and local to this machine; a teammate/CI box needs
   its own copy (either re-run the download steps, or install `allure-commandline` normally).
5. **Employee/Leave/Attendance/Payroll coverage is intentionally partial** — see each module's own
   "Deliberately not covered this batch" note above. Scaling any of the four further needs a
   deliberate next step.
6. **Master data dependency for the bank-details test**: `test_bank_detail_crud_lifecycle` skips
   if the tenant has zero active Bank master records — not verified either way in this environment
   since the suite never reached it (credentials skip first). **Update (2026-09-29):** ANEVRA01 now
   has one active bank, `QAAUTO-BANK` (Development-only seeder, `docs/qa/payroll-sandbox-setup.md`
   §4.1), so this test no longer skips for that reason there. It picks the first active bank, which is
   `QAAUTO-BANK`. **Re-run 2026-09-29: passed** (invalid bank id 400; create 201 on `QAAUTO-BANK`;
   masked on read; update; delete; the disposable employee was removed).
7. ~~No dedicated Attendance-capable QA identities exist yet~~ **Resolved in Phase 6** — QA Admin/
   Employee/Manager now exist, are linked, and QA Manager is QA Employee's manager in
   `EmployeeEmploymentHistory`. What remains genuinely blocked is **not** identity provisioning but
   the authorization gap itself (see Phase 6's "New authorization gap found this session"): the
   seeded `Employee`/`Manager` roles still cannot reach `Attendance.View`-gated routes
   (self-service calendar/day, manager team roster) or the Regularization/On Duty
   submit/approve actions (CR-111/CR-112). A true two-identity Attendance approve/reject happy path
   is still not exercisable under current seeded permissions — not because no second identity
   exists (it does — QA Manager), but because neither identity is granted the permission the
   relevant controller actions require. Elevating either role was deliberately not done; this is
   the reported gap for a human/product decision.
8. **No payroll run can currently reach `Calculated` in ANEVRA01, for any period** (Phase 7 finding
   — see "Tests (Payroll smoke suite, Phase 7)" above): `PayrollCalculationEngine.
   CalculateEmployeeAsync` unconditionally requires a finalized (`Closed`) `AttendancePeriod`
   matching the Payroll Period's exact dates, via the Overtime snapshot resolver, regardless of
   whether the Salary Structure has any Overtime/Attendance-driven component. This blocks Approve,
   Finalize, Bank Advice generation, and GL/Accounting posting as a positive/happy path for *any*
   future Payroll batch too, not just this one — a human/product decision is needed on whether to
   provision Attendance Foundation + a matching finalized Monthly period, or make the Overtime
   resolution call conditional on the structure actually needing it. Not fixed here, per this
   task's "reporting, not fixing" precedent (see Phase 6's Attendance authorization-gap entry
   above for the same practice). **Update (2026-09-29):** a DEV/TEST-only prerequisite sandbox now
   reaches `Calculated` → `Approved` plus Bank Advice and GL generation without any engine change,
   using a reserved 1950s attendance/payroll window: `sandbox/payroll_sandbox.py`, documented in
   `docs/qa/payroll-sandbox-setup.md`. The smoke suite itself is unchanged. The same path is now
   automated as a pytest E2E; see "Tests (Payroll happy-path E2E on the QAAUTO sandbox)".
9. **QAAUTO Payroll employees cannot be deleted once used** (Phase 7 finding) — `EmployeeService.
   DeleteAsync` correctly rejects deletion (`409`, `DbUpdateException` from the referencing FK) once
   an employee has an Employee Salary Assignment or Payroll Run history. Every employee the Payroll
   smoke suite creates is, in practice, a permanent tenant record. Real product behavior, not a
   framework defect — noted so a human doesn't mistake tenant growth over repeated runs for a leak.

---

ATTENDANCE AUTOMATION SMOKE: COMPLETE

PAYROLL AUTOMATION SMOKE: COMPLETE

QA AUTOMATION TEST ENVIRONMENT: READY
