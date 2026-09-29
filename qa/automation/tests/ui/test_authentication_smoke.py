"""UI smoke suite for Module 01 — Authentication.

Proof-of-concept coverage, automated from qa/01-authentication/cases/*.yaml. Test titles carry
the manual case id they trace to for traceability; several cases are exercised implicitly (e.g. a
UI test that reaches the same code path as more than one manual case).

Cases covered here:
  - AUTH-LOGIN-001  (01-tenant-login.yaml)        — valid credentials sign the user in
  - AUTH-LOGIN-*    (01-tenant-login.yaml)        — invalid credentials are rejected
  - AUTH-REFRESH-016 (03-refresh-tokens.yaml)      — logout ends the session
  - TENANT-HOST-002 (09-tenant-host-resolution-and-cross-tenant.yaml) — unknown host is not served as a tenant
  - RequireAuth guard (src/auth/RequireAuth.tsx)  — protected page redirects an anonymous visitor

Requires QA_TENANT_A_HOST always; QA_A_ADMIN_IDENTIFIER / QA_A_ADMIN_PASSWORD for the tests that
sign in. Missing variables skip (not fail) the tests that need them — see utils/env_utils.py.
"""

from __future__ import annotations

import allure
import pytest

from data.test_data import KNOWN_INVALID_IDENTIFIER, KNOWN_INVALID_PASSWORD, admin_user_a
from pages.dashboard_page import DashboardPage
from pages.login_page import LoginPage
from utils.env_utils import require_env

pytestmark = [allure.feature("Authentication"), pytest.mark.ui]


@allure.story("Login page")
@allure.title("Login page loads for a known tenant host")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_login_page_loads(page, settings):
    host = require_env("QA_TENANT_A_HOST")
    login_page = LoginPage(page).open(settings.ui_url(host, "/login"))

    assert login_page.is_loaded(), "Expected the login page shell (data-testid=login-page) to render"
    assert page.locator(LoginPage.IDENTIFIER_INPUT).is_visible()
    assert page.locator(LoginPage.PASSWORD_INPUT).is_visible()
    assert page.locator(LoginPage.SUBMIT_BUTTON).is_visible()


@allure.story("Login")
@allure.title("AUTH-LOGIN-001 — valid credentials sign the user in and land on the dashboard")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_valid_login_succeeds(page, settings):
    host = require_env("QA_TENANT_A_HOST")
    user = admin_user_a()

    login_page = LoginPage(page).open(settings.ui_url(host, "/login"))
    login_page.login(user.identifier, user.password)

    page.wait_for_url("**/dashboard", timeout=15_000)
    dashboard = DashboardPage(page)
    assert dashboard.is_loaded(), "Expected a signed-in header with a Sign out control after a valid login"


@allure.story("Login")
@allure.title("Invalid credentials are rejected with a visible error and no navigation")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_invalid_credentials_are_rejected(page, settings):
    host = require_env("QA_TENANT_A_HOST")

    login_page = LoginPage(page).open(settings.ui_url(host, "/login"))
    login_page.login(KNOWN_INVALID_IDENTIFIER, KNOWN_INVALID_PASSWORD)

    with allure.step("Expect a visible sign-in error and no navigation away from /login"):
        page.wait_for_selector(LoginPage.FORM_ERROR, timeout=10_000)
        assert "/login" in page.url
        assert login_page.error_message()


@allure.story("Tenant host resolution")
@allure.title("TENANT-HOST-002 — an unregistered host does not present a workspace to sign in to")
@pytest.mark.smoke
@pytest.mark.regression
def test_unknown_tenant_host(page, settings):
    login_page = LoginPage(page).open(settings.ui_url(settings.unknown_host, "/login"))

    assert login_page.is_workspace_unavailable(), (
        "Expected a 'workspace not found/unavailable' state for a host with no organization"
    )


@allure.story("Session")
@allure.title("AUTH-REFRESH-016 — signing out returns the user to the login page")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_logout_redirects_to_login(page, settings):
    host = require_env("QA_TENANT_A_HOST")
    user = admin_user_a()

    login_page = LoginPage(page).open(settings.ui_url(host, "/login"))
    login_page.login(user.identifier, user.password)
    page.wait_for_url("**/dashboard", timeout=15_000)

    DashboardPage(page).sign_out()

    page.wait_for_url("**/login", timeout=15_000)
    login_page = LoginPage(page)
    page.wait_for_selector(f"{LoginPage.ROOT}, {LoginPage.UNAVAILABLE_TITLE}", timeout=10_000)
    assert login_page.is_loaded()


@allure.story("Route protection")
@allure.title("An unauthenticated visitor requesting a protected page is redirected to login")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_protected_page_redirects_unauthenticated_user(page, settings):
    host = require_env("QA_TENANT_A_HOST")

    page.goto(settings.ui_url(host, "/dashboard"))

    page.wait_for_url("**/login", timeout=15_000)
    login_page = LoginPage(page)
    page.wait_for_selector(f"{LoginPage.ROOT}, {LoginPage.UNAVAILABLE_TITLE}", timeout=10_000)
    assert login_page.is_loaded()
