# QA automation CI

How the Python + Playwright + pytest + Allure suite in `qa/automation` runs in GitHub Actions.

- Workflow: `.github/workflows/qa-automation.yml`
- CI entry point: `qa/automation/ci/run_ci.py`. Every workflow step calls it, so you can run any step locally.
- Auth rate pacing: `qa/automation/utils/auth_rate_guard.py`

The workflow is manual-only for now (`workflow_dispatch`). It does not start or change the HRMS
application. It tests the stack that is already running on the runner's machine.

## Why a self-hosted runner

The automation cannot target a remote environment as written:

- API tests always connect to `localhost:<QA_API_PORT>` and send the tenant in the `Host` header
  (`config/settings.py`, `core/api_client.py`).
- The payroll sandbox refuses to write unless the API is local and a Development server (preflight
  G2 and G4 in `sandbox/payroll_sandbox.py`).
- The sandbox identity is fixed to tenant `ANEVRA01` at `anevra01.localhost`, with the QA
  identities from the Development-only seeder.

So the blocking jobs run on a self-hosted runner on the machine that hosts the API (port 5080), the
Vite UI (port 5173) and MySQL. The `collect` and `report` jobs need no stack and no secrets, so they
run on GitHub-hosted `ubuntu-latest`.

## Jobs

| Job | Runner | Blocking | What it does |
|---|---|---|---|
| `collect` | ubuntu-latest | yes | Installs dependencies, runs the CI tooling unit tests (`ci/test_run_ci.py`), then collects every test and checks that the three suites partition them (no overlap, no gap, none empty). Needs no secrets. |
| `smoke` | self-hosted | yes | Installs dependencies and Chromium, runs preflight, then runs `not known_defect and not e2e`: every API and UI smoke module. |
| `e2e` | self-hosted | yes | Preflight, then `e2e and not known_defect`: the payroll happy-path and the GL/Analytics E2E modules. They write only to the QAAUTO sandbox. The `run_e2e` input turns this job off. |
| `known-defects` | self-hosted | no (`continue-on-error`) | Runs `known_defect`: checks that assert the correct contract for reported defects, so they fail while the defect exists. The `run_known_defects` input turns this job off. |
| `report` | ubuntu-latest | no (`continue-on-error`) | Downloads the result artifacts and builds two single-file Allure reports: `blocking` (smoke + e2e) and `known-defects`. The two stay separate. |

The jobs run one after another (`needs`, plus the `qa-automation` concurrency group, which never
cancels a running workflow). Two reasons: every sign-in from the runner shares one rate-limit
partition, and both E2E modules share one sandbox manifest.

`e2e` and `known-defects` still run when `smoke` fails, because each one reports on its own. The
workflow fails when `collect`, `smoke` or `e2e` fails.

## What fails the build

A blocking suite (`smoke`, `e2e`) fails when any of these happens:

- a test fails or errors (every blocking test is outside `known_defect`, so any failure is unexpected)
- the auth rate limiter returns 429 to any request, or a skip or failure message shows `(429)`. The
  identity fixtures turn a 429 sign-in into a skip, so such a run is only green on paper.
- every selected test skips (for example, the credentials are wrong)
- pytest exits with a status other than 0 or 1 (interrupted, internal error, usage error, nothing collected)

Other skips are data or environment conditions that the tests report on purpose (for example, "no
submittable Leave Type", or the E2E stage-09 self-approval skip). They do not fail the build. The
job summary lists them by reason.

The `known-defects` job always exits 0. Its summary splits the results into these groups:

- **still failing as expected**
- **now passing**: the defect may be fixed. The job also raises a warning annotation. Confirm the
  fix, then remove the test's `known_defect` marker so the test joins a blocking suite.
- **errored in setup**
- **skipped**

## Avoiding the auth rate limit

The API allows 20 requests per 60 seconds for each (tenant, client IP) pair. The limit covers
`POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/set-password` and
`GET /api/tenants/current/branding` (`Backend/HRMS.API/Security/RateLimitingPolicies.cs`). The
limit is a production security control. Do not raise it to make CI green. CI stays under it in
three ways:

1. **Sequential execution.** There is no `-n`/xdist. Modules run one after another in a single
   pytest process, and the jobs themselves run one after another.
2. **API session reuse** (`QA_REUSE_API_SESSIONS=1`). `admin_api_client`, `employee_api_client`
   and `manager_api_client` sign in once per identity and reuse the access token until it is 5
   minutes from expiry (tokens last 60 minutes). Access tokens are stateless JWTs, so a logout or
   sign-in in another test does not invalidate them. Tests that exercise login, refresh and logout
   themselves still make their own calls. UI tests still sign in through the browser each time.
   Refresh tokens are single-use, so one browser storage state cannot be shared between tests.
3. **Pacing** (`QA_AUTH_RATE_BUDGET=18`). Every limited request is recorded, from `ApiClient` and
   from the browser. Before each test's setup, the guard waits until the trailing 62-second window
   holds no more than `budget − reserve` requests. The reserve is 8 for a browser test and 6 for an
   API test. That leaves room for the test's own worst case under 18, and 18 is 2 under the
   server's 20.

Each run writes `reports/ci/<suite>/auth-rate-ledger.json`. It records the requests per test, the
waits, and any 429. If a test goes over its reserve, the log shows a warning. In that case, raise
`QA_AUTH_RATE_RESERVE_UI` or `QA_AUTH_RATE_RESERVE_API`.

Without these environment variables, a local `pytest` run behaves exactly as before.

## Required secrets

Store these as secrets of the GitHub environment `qa` (Settings → Environments → `qa`). Repository
secrets also work. The jobs reference `environment: qa`, so you can add required reviewers there if
you want a manual approval gate. Use only the disposable QA identities from the Phase 6 section of
`qa/automation/README.md`. Never use a real or production account.

| Secret | Used by | Notes |
|---|---|---|
| `QA_A_ADMIN_USERNAME` | smoke, e2e, known-defects | QA Admin (TenantAdmin). For ANEVRA01, the email `qaauto-admin@anevra01.qa-automation.invalid`. The sandbox preflight (G6) requires exactly this actor. |
| `QA_A_ADMIN_PASSWORD` | smoke, e2e, known-defects | |
| `QA_A_EMPLOYEE_USERNAME` | smoke | QA Employee, by employee code (`QAAUTO-EMP` on ANEVRA01) |
| `QA_A_EMPLOYEE_PASSWORD` | smoke | |
| `QA_A_MANAGER_USERNAME` | smoke | QA Manager, by employee code (`QAAUTO-MGR` on ANEVRA01) |
| `QA_A_MANAGER_PASSWORD` | smoke | |

Nothing in the repository holds a credential. `qa/automation/.env` is git-ignored and CI never
reads it (a fresh checkout has none). Preflight checks only that each variable is present. It never
prints a value.

## Repository variables

Settings → Secrets and variables → Actions → Variables. All of them are optional except
`QA_SANDBOX_STATE_DIR`, which the `e2e` job needs.

| Variable | Default | Purpose |
|---|---|---|
| `QA_RUNNER_LABELS` | `["self-hosted","hrms-qa"]` | JSON label list of the runner for the three stack jobs |
| `QA_TENANT_A_HOST` | `anevra01.localhost` | Tenant host under test |
| `QA_SCHEME` / `QA_API_PORT` / `QA_UI_PORT` | `http` / `5080` / `5173` | Local stack address |
| `QA_SANDBOX_STATE_DIR` | none | Directory on the runner that holds `payroll-sandbox-manifest.json`. Required for `e2e`; `known-defects` reads it too. |
| `QA_PAYROLL_SANDBOX_TENANT_ID` | none | Optional tenant-id pin for sandbox preflight G5 |
| `QA_AUTH_RATE_BUDGET` | `18` | Pacing budget per window; see above |
| `QA_PYTHON_VERSION` | `3.14` | Python for `actions/setup-python` (the suite was validated on 3.14.0) |

### The sandbox manifest

The E2E modules find their rows through `payroll-sandbox-manifest.json`. On a fresh checkout it
would be empty. The file is git-ignored, and `actions/checkout` removes ignored files. Preflight G7
then refuses every write, because the tenant already holds sandbox months that an empty manifest
does not own.

`QA_SANDBOX_STATE_DIR` moves the manifest out of the checkout. Only one manifest may exist per
tenant. So on the machine where you already run the sandbox by hand, set `QA_SANDBOX_STATE_DIR` to
the directory that already holds it (by default `D:\HRMS\qa\automation\.state`), or move it once to
a stable path and use that path for both manual runs and CI. Do not run the sandbox CLI while the
workflow runs.

Each E2E run uses 2 of the 120 reserved months (1950-01 to 1959-12). 10 were used before CI
existed. Clear the `run_e2e` input when you do not need the E2E coverage.

## One-time runner setup

1. On the machine that hosts the stack, register a self-hosted runner for this repository with the
   label `hrms-qa` (Settings → Actions → Runners → New self-hosted runner). Run the runner as the
   same Windows user who runs the stack. `*.localhost` must resolve to loopback, which it does on
   Windows 10 and later.
2. The runner needs `git` and outbound internet access for pip, the Playwright download and GitHub
   artifacts. `actions/setup-python` installs Python into the runner tool cache. If that fails on
   Windows because of permissions, install Python 3.14 into the tool cache once, or run the runner
   with the rights it needs. On a Linux runner, install the browser system libraries once:
   `python -m playwright install-deps chromium` (needs root).
3. Before you trigger the workflow, start the stack as usual: MySQL, then
   `dotnet run --project Backend/HRMS.API` (Development), then `npm run dev` in
   `Frontend/HRMS.Web`. The E2E Bank Advice stages also need `DevelopmentSeed:EnableQaAutomationBank=true`
   (see `docs/qa/payroll-sandbox-setup.md` §4.1).
4. Add the secrets and `QA_SANDBOX_STATE_DIR` (see above).
5. Commit `qa/automation` and `.github/workflows/qa-automation.yml` to the default branch. GitHub
   shows a `workflow_dispatch` workflow only after it exists on the default branch. Then run it from
   Actions → QA automation → Run workflow.

## Artifacts

| Artifact | Contents |
|---|---|
| `qa-collect` | Collection summary |
| `qa-smoke-results`, `qa-e2e-results`, `qa-known-defects-results` | `junit.xml`, `allure-results/`, `pytest.log`, `auth-rate-ledger.json`, `summary.md`, plus Playwright failure screenshots and traces (`test-results/<suite>/`) |
| `qa-allure-report` | `blocking/index.html` and `known-defects/index.html`, single-file Allure reports |

Artifacts are kept for 7 days. Playwright traces record typed values and full request and response
bodies, so an unredacted trace contains the sign-in password. Before each upload, the `scrub` step
does these things:

- It rewrites every trace zip and report file, and masks each `QA_*PASSWORD` value in its raw and
  JSON-escaped forms.
- It masks every JWT.
- It masks `accessToken`, `refreshToken` and password fields.
- It masks bearer headers.

The upload step runs only if `scrub` succeeded. Screenshots are left untouched, because password
fields render masked. Console logs are separate: GitHub masks the registered secrets there, but not
the dynamic tokens that a failing assertion can print. Those tokens are valid only against the
runner's local API.

## Running the same steps locally

From `qa/automation`, with the venv active and `.env` filled in:

```powershell
python -m pytest ci -o addopts="" -q            # CI tooling unit tests
python ci/run_ci.py collect                     # collection + partition check
python ci/run_ci.py preflight --suite smoke     # variables present, API and UI reachable
python ci/run_ci.py run --suite smoke           # paces and reuses sessions like CI
python ci/run_ci.py run --suite known-defects
python ci/run_ci.py scrub
```

`run_ci.py` loads `.env` the same way `conftest.py` does. A variable already set in the
environment wins. For `run --suite e2e`, also set `QA_PAYROLL_SANDBOX=1` and
`QA_PAYROLL_SANDBOX_TENANT=ANEVRA01`.

Outputs go to `reports/ci/<suite>/` and `test-results/<suite>/` (both git-ignored). To pass extra
pytest arguments, set `QA_CI_EXTRA_PYTEST_ARGS`, for example `-k login`. That variable is what the
workflow's `pytest_args` input sets.
