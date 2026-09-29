"""Session-wide fixtures and hooks for the HRMS QA automation framework.

- Loads .env (if present) before anything else reads os.environ.
- Puts this directory on sys.path so tests can `from config...`, `from core...`, etc. regardless
  of which directory pytest is invoked from.
- Provides `settings` and `api_client_factory` fixtures used across UI and API tests.
- Writes an Allure environment.properties file so every report records what it ran against
  (never a credential value — only whether one is configured).
- On a failing test: attaches a screenshot + URL (UI, via the `page` fixture), any captured
  browser console/page errors (UI), and a redacted request/response log (API, via
  `api_client_factory`) to the Allure result — on top of pytest-playwright's own
  --screenshot/--tracing artifacts (configured in pytest.ini).
- Paces tests under the backend's auth rate limit when QA_AUTH_RATE_BUDGET is set, and reuses one
  API sign-in per identity when QA_REUSE_API_SESSIONS is set (both are what CI sets; see
  utils/auth_rate_guard.py and docs/qa/automation-ci.md). Unset, a run behaves as before.
"""

from __future__ import annotations

import base64
import importlib.metadata as importlib_metadata
import json
import os
import platform
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import allure
import pytest
from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parent
load_dotenv(ROOT / ".env")

if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from config.settings import Settings, load_settings  # noqa: E402
from utils import auth_rate_guard  # noqa: E402
from utils.env_utils import is_env_configured  # noqa: E402
from utils.logger import get_logger  # noqa: E402

log = get_logger(__name__)


@pytest.fixture(scope="session")
def settings() -> Settings:
    return load_settings()


@pytest.fixture
def api_client_factory(settings: Settings, request: pytest.FixtureRequest):
    """Returns a factory `make(host) -> ApiClient` bound to the current settings.

    Every client created shares one history list, stashed on the test node, so a failing test's
    request/response evidence can be attached to Allure regardless of how many clients it made.
    """
    from core.api_client import ApiClient

    history: list = getattr(request.node, "_qa_api_history", None)
    if history is None:
        history = []
        request.node._qa_api_history = history

    def _make(host: str) -> "ApiClient":
        return ApiClient(settings, host, history=history)

    return _make


def _reuse_api_sessions() -> bool:
    return os.environ.get("QA_REUSE_API_SESSIONS", "").strip().lower() in ("1", "true", "yes", "on")


def _token_expiry(access_token: str) -> float:
    payload = access_token.split(".")[1]
    payload += "=" * (-len(payload) % 4)
    return float(json.loads(base64.urlsafe_b64decode(payload)).get("exp", 0))


@pytest.fixture(scope="session")
def _api_session_tokens() -> dict:
    """(host, identifier) -> (access token, exp). Filled only when QA_REUSE_API_SESSIONS is set."""
    return {}


def _signed_in_client(api_client_factory, tokens: dict, user, env_prefix: str):
    """An ApiClient bound to QA_TENANT_A_HOST with a bearer token for `user`.

    With QA_REUSE_API_SESSIONS set, the first test signs in and later tests reuse that access token
    until it is within 5 minutes of expiry. Access tokens are stateless JWTs, so another test's logout
    or sign-in never invalidates them. Otherwise every test signs in, as before. Skips (never
    fabricates a session) when signing in fails — a setup problem, not the module under test.
    """
    from core.auth_helpers import api_login
    from utils.env_utils import require_env

    host = require_env("QA_TENANT_A_HOST")
    client = api_client_factory(host)
    key = (host, user.identifier)
    cached = tokens.get(key) if _reuse_api_sessions() else None
    if cached and cached[1] - time.time() > 300:
        return client.with_bearer(cached[0])

    response = api_login(client, user.identifier, user.password)
    if response.status_code != 200:
        pytest.skip(
            f"Could not sign in as the configured {env_prefix} user ({response.status_code}) — "
            f"check {env_prefix}_USERNAME/{env_prefix}_PASSWORD. See the attached request/response log."
        )
    token = response.json()["data"]["accessToken"]
    if _reuse_api_sessions():
        tokens[key] = (token, _token_expiry(token))
    return client.with_bearer(token)


@pytest.fixture
def admin_api_client(api_client_factory, _api_session_tokens):
    """An ApiClient bound to QA_TENANT_A_HOST, authenticated as `data.test_data.admin_user_a()`.

    Skips (never fabricates a session) when the tenant host or admin credentials aren't
    configured, or when signing in with them fails — a setup problem, not the module under test.
    """
    from data.test_data import admin_user_a

    return _signed_in_client(api_client_factory, _api_session_tokens, admin_user_a(), "QA_A_ADMIN")


@pytest.fixture
def employee_api_client(api_client_factory, _api_session_tokens):
    """An ApiClient bound to QA_TENANT_A_HOST, authenticated as `data.test_data.employee_user_a()`
    — a linked employee holding only the plain, self-service `Employee` role (Leave.RequestCreate/
    RequestViewOwn/RequestWithdrawOwn/RequestCancelOwn/BalanceViewOwn/TypeViewAvailable — see
    SeedData.cs RolePermissionMap[RoleNames.Employee]). Used for Leave self-service checks, where
    an admin/TenantAdmin account is the wrong credential: self-service Leave endpoints resolve the
    caller's own linked Employee identity, and a TenantAdmin seed user is not necessarily one.

    Skips (never fabricates a session) when QA_A_EMPLOYEE_USERNAME/PASSWORD aren't configured, or
    signing in with them fails.
    """
    from data.test_data import employee_user_a

    return _signed_in_client(api_client_factory, _api_session_tokens, employee_user_a(), "QA_A_EMPLOYEE")


@pytest.fixture
def manager_api_client(api_client_factory, _api_session_tokens):
    """An ApiClient bound to QA_TENANT_A_HOST, authenticated as `data.test_data.manager_user_a()`
    — holds `Leave.Approve` (see SeedData.cs RolePermissionMap[RoleNames.Manager]). Used for the
    Leave approval-inbox checks.

    Skips (never fabricates a session) when QA_A_MANAGER_USERNAME/PASSWORD aren't configured, or
    signing in with them fails.
    """
    from data.test_data import manager_user_a

    return _signed_in_client(api_client_factory, _api_session_tokens, manager_user_a(), "QA_A_MANAGER")


@pytest.fixture
def ui_signed_in_employee(page, settings: Settings):
    """Like `ui_signed_in`, but as `data.test_data.employee_user_a()` — a linked employee holding
    only the plain, self-service `Employee` role. Used for Leave self-service UI pages, which need
    a resolvable employee identity that a TenantAdmin seed user does not necessarily have.

    Skips when QA_A_EMPLOYEE_USERNAME/PASSWORD aren't configured, or sign-in fails.
    """
    from data.test_data import employee_user_a
    from pages.login_page import LoginPage
    from utils.env_utils import require_env

    host = require_env("QA_TENANT_A_HOST")
    user = employee_user_a()

    login_page = LoginPage(page).open(settings.ui_url(host, "/login"))
    login_page.login(user.identifier, user.password, remember_me=True)
    try:
        page.wait_for_url("**/dashboard", timeout=15_000)
    except Exception:
        pytest.skip(f"UI sign-in as the configured QA_A_EMPLOYEE user did not reach the dashboard (still at {page.url}).")
    return host


@pytest.fixture
def ui_signed_in(page, settings: Settings):
    """Signs the browser in as `data.test_data.admin_user_a()`, with Remember Me checked so the
    refresh token persists in localStorage — letting a test `page.goto()` any protected route
    directly afterward (a full navigation would otherwise drop the in-memory access token) instead
    of having to click through the app's own navigation every time.

    Skips when QA_TENANT_A_HOST / admin credentials aren't configured, or sign-in fails.
    """
    from data.test_data import admin_user_a
    from pages.login_page import LoginPage
    from utils.env_utils import require_env

    host = require_env("QA_TENANT_A_HOST")
    user = admin_user_a()

    login_page = LoginPage(page).open(settings.ui_url(host, "/login"))
    login_page.login(user.identifier, user.password, remember_me=True)
    try:
        page.wait_for_url("**/dashboard", timeout=15_000)
    except Exception:
        pytest.skip(f"UI sign-in as the configured QA_A_ADMIN user did not reach the dashboard (still at {page.url}).")
    return host


@pytest.fixture
def created_employee(admin_api_client):
    """Creates one disposable employee (personal-details path: unique name, today's join date) for
    a test that needs an existing record, and deletes it afterward (best-effort — see README "Test
    data and cleanup"). Never touches any employee this suite did not itself create.
    """
    from core.employee_api import create_personal_details, delete_employee
    from data.test_data import today_iso, unique_last_name

    first_name = "QaAuto"
    last_name = unique_last_name()
    response = create_personal_details(
        admin_api_client,
        firstName=first_name,
        lastName=last_name,
        dateOfJoining=today_iso(),
    )
    if response.status_code != 201:
        pytest.fail(
            f"Setup failed: could not create the disposable employee this test needs "
            f"({response.status_code}): {response.text}"
        )
    employee = response.json()["data"]

    yield employee

    try:
        delete_response = delete_employee(admin_api_client, employee["id"])
        if delete_response.status_code != 200:
            log.warning(
                "Cleanup: deleting employee %s returned %s: %s",
                employee["id"], delete_response.status_code, delete_response.text,
            )
    except Exception:
        log.warning("Cleanup: deleting employee %s raised an exception", employee["id"], exc_info=True)


@pytest.fixture(autouse=True)
def _capture_console_errors(request: pytest.FixtureRequest):
    """Records browser console errors / uncaught page exceptions for UI tests, without forcing a
    browser launch for API-only tests (checks the fixture closure instead of requesting `page`)."""
    if "page" not in request.fixturenames:
        yield
        return

    page = request.getfixturevalue("page")
    errors: list[str] = []

    def _on_console(msg) -> None:
        if msg.type == "error":
            errors.append(f"console.error: {msg.text}")

    def _on_pageerror(exc) -> None:
        errors.append(f"pageerror: {exc}")

    page.on("console", _on_console)
    page.on("pageerror", _on_pageerror)
    request.node._qa_console_errors = errors
    yield


@pytest.fixture(autouse=True)
def _track_browser_auth_requests(request: pytest.FixtureRequest):
    """Feeds browser requests to auth-limited endpoints into utils.auth_rate_guard, so UI sign-ins,
    token refreshes and branding reads count toward the same pacing budget as API sign-ins. Like
    `_capture_console_errors`, never launches a browser for an API-only test."""
    if "page" not in request.fixturenames:
        yield
        return

    context = request.getfixturevalue("page").context

    def _on_response(response) -> None:
        auth_rate_guard.record(response.url, response.request.method, response.status)

    def _on_request_failed(req) -> None:
        auth_rate_guard.record(req.url, req.method)

    context.on("response", _on_response)
    context.on("requestfailed", _on_request_failed)
    yield


@pytest.hookimpl(tryfirst=True)
def pytest_runtest_setup(item: pytest.Item) -> None:
    auth_rate_guard.begin_test(item.nodeid, is_ui="page" in getattr(item, "fixturenames", ()))


def pytest_runtest_logfinish(nodeid: str, location) -> None:
    auth_rate_guard.end_test()


def pytest_sessionfinish(session: pytest.Session, exitstatus: int) -> None:
    auth_rate_guard.write_ledger()


@pytest.fixture(scope="session")
def browser_type_launch_args(browser_type_launch_args: dict) -> dict:
    """Honor QA_HEADLESS when explicitly set, without fighting pytest-playwright's --headed flag
    when it isn't (see config/settings.py: headless_override is None unless QA_HEADLESS is set)."""
    settings_ = load_settings()
    if settings_.headless_override is None:
        return browser_type_launch_args
    return {**browser_type_launch_args, "headless": settings_.headless_override}


def _package_version(name: str) -> str:
    try:
        return importlib_metadata.version(name)
    except importlib_metadata.PackageNotFoundError:
        return "(not installed)"


def _git_commit() -> str:
    try:
        result = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=5,
        )
        return result.stdout.strip() or "(unknown)"
    except Exception:
        return "(unknown)"


def pytest_configure(config: pytest.Config) -> None:
    configured_dir = config.getoption("allure_report_dir", default=None)
    results_dir = Path(configured_dir) if configured_dir else ROOT / "reports" / "allure-results"
    results_dir.mkdir(parents=True, exist_ok=True)
    settings_ = load_settings()

    browsers = config.getoption("--browser", default=None) or ["chromium"]
    headless = (
        "explicit:" + str(settings_.headless_override)
        if settings_.headless_override is not None
        else ("headed" if config.getoption("--headed", default=False) else "headless (default)")
    )

    lines = [
        f"Run.TimestampUtc={datetime.now(timezone.utc).isoformat(timespec='seconds')}",
        f"Repo.Commit={_git_commit()}",
        f"UI.BaseHostPort=<tenant-host>:{settings_.ui_port}",
        f"API.BaseHostPort=localhost:{settings_.api_port}",
        f"Tenant.Host={os.environ.get('QA_TENANT_A_HOST', '(not set)')}",
        f"Unknown.Host={settings_.unknown_host}",
        f"Browser={','.join(browsers)}",
        f"Browser.Mode={headless}",
        f"Credentials.AdminConfigured={is_env_configured('QA_A_ADMIN_PASSWORD') and (is_env_configured('QA_A_ADMIN_USERNAME') or is_env_configured('QA_A_ADMIN_IDENTIFIER'))}",
        f"Credentials.EmployeeRoleConfigured={is_env_configured('QA_A_EMPLOYEE_USERNAME', 'QA_A_EMPLOYEE_PASSWORD')}",
        f"Credentials.ManagerRoleConfigured={is_env_configured('QA_A_MANAGER_USERNAME', 'QA_A_MANAGER_PASSWORD')}",
        f"Run.CI={os.environ.get('CI', 'false')}",
        f"Run.AuthRateBudget={auth_rate_guard.budget() or '(no pacing)'}",
        f"Run.ApiSessionReuse={_reuse_api_sessions()}",
        f"OS={platform.platform()}",
        f"Python={platform.python_version()}",
        f"pytest={_package_version('pytest')}",
        f"playwright={_package_version('playwright')}",
        f"pytest-playwright={_package_version('pytest-playwright')}",
        f"allure-pytest={_package_version('allure-pytest')}",
    ]
    (results_dir / "environment.properties").write_text("\n".join(lines) + "\n", encoding="utf-8")


@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item: pytest.Item, call: pytest.CallInfo):
    outcome = yield
    report = outcome.get_result()
    if report.when != "call" or not report.failed:
        return

    page = item.funcargs.get("page") if hasattr(item, "funcargs") else None
    if page is not None:
        try:
            allure.attach(
                page.screenshot(full_page=True), name="failure-screenshot", attachment_type=allure.attachment_type.PNG
            )
        except Exception:  # pragma: no cover - best-effort diagnostic, must never mask the real failure
            pass
        try:
            allure.attach(page.url, name="failure-url", attachment_type=allure.attachment_type.TEXT)
        except Exception:  # pragma: no cover
            pass

    console_errors = getattr(item, "_qa_console_errors", None)
    if console_errors:
        allure.attach(
            "\n".join(console_errors), name="browser-console-errors", attachment_type=allure.attachment_type.TEXT
        )

    api_history = getattr(item, "_qa_api_history", None)
    if api_history:
        import json

        formatted = json.dumps(api_history, indent=2, default=str)
        allure.attach(
            formatted, name="api-request-response-log (redacted)", attachment_type=allure.attachment_type.JSON
        )
