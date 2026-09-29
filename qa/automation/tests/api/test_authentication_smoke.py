"""API smoke suite for Module 01 — Authentication (direct HTTP, no browser).

Proof-of-concept coverage, automated from qa/01-authentication/cases/*.yaml:
  - AUTH-LOGIN-001   (01-tenant-login.yaml)                                — valid login returns a token pair
  - AUTH-LOGIN-*     (01-tenant-login.yaml)                                — invalid credentials -> 401
  - AUTH-REFRESH-016 (03-refresh-tokens.yaml)                              — refresh after logout -> 401
  - TENANT-HOST-002  (09-tenant-host-resolution-and-cross-tenant.yaml)     — login at an unregistered host -> 401
  - Protected-endpoint 401 (AuthController [Authorize] convention)

Requires QA_TENANT_A_HOST always; QA_A_ADMIN_IDENTIFIER / QA_A_ADMIN_PASSWORD for the tests that
sign in. Missing variables skip (not fail) the tests that need them — see utils/env_utils.py.
"""

from __future__ import annotations

import allure
import pytest

from core.auth_helpers import api_login, api_logout, api_refresh
from data.test_data import KNOWN_INVALID_IDENTIFIER, KNOWN_INVALID_PASSWORD, admin_user_a
from utils.env_utils import require_env

pytestmark = [allure.feature("Authentication"), pytest.mark.api]


@allure.story("Login")
@allure.title("AUTH-LOGIN-001 — valid credentials return a well-formed token pair")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_valid_login_returns_tokens(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    user = admin_user_a()
    client = api_client_factory(host)

    response = api_login(client, user.identifier, user.password)

    assert response.status_code == 200, response.text
    body = response.json()
    assert body["success"] is True
    data = body["data"]
    assert data["accessToken"].count(".") == 2, "accessToken should be a 3-part JWT"
    assert data["refreshToken"]
    assert data["tokenType"] == "Bearer"


@allure.story("Login")
@allure.title("Invalid credentials return 401 with a generic message")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_invalid_credentials_return_401(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    client = api_client_factory(host)

    response = api_login(client, KNOWN_INVALID_IDENTIFIER, KNOWN_INVALID_PASSWORD)

    assert response.status_code == 401, response.text
    assert response.json()["success"] is False


@allure.story("Session")
@allure.title("AUTH-REFRESH-016 — logout revokes the refresh token; a later refresh is rejected")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_logout_then_refresh_is_rejected(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    user = admin_user_a()
    client = api_client_factory(host)

    login_response = api_login(client, user.identifier, user.password)
    assert login_response.status_code == 200, login_response.text
    tokens = login_response.json()["data"]

    logout_response = api_logout(client, tokens["refreshToken"], tokens["accessToken"])
    assert logout_response.status_code == 200, logout_response.text

    refresh_response = api_refresh(client, tokens["refreshToken"])
    assert refresh_response.status_code == 401, refresh_response.text


@allure.story("Route protection")
@allure.title("A protected endpoint refuses a request with no bearer token")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_protected_endpoint_requires_authentication(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    client = api_client_factory(host)

    response = client.get("/api/employees")

    assert response.status_code == 401, response.text


@allure.story("Tenant host resolution")
@allure.title("TENANT-HOST-002 — login at an unregistered host is refused, not silently accepted")
@pytest.mark.smoke
@pytest.mark.regression
def test_login_at_unknown_host_is_refused(api_client_factory, settings):
    client = api_client_factory(settings.unknown_host)

    response = api_login(client, KNOWN_INVALID_IDENTIFIER, KNOWN_INVALID_PASSWORD)

    allure.attach(
        f"status={response.status_code}\nbody={response.text}",
        name="unknown-host-login-response",
        attachment_type=allure.attachment_type.TEXT,
    )
    assert response.status_code == 401, (
        f"TENANT-HOST-002 expects 401 for an unregistered host; got {response.status_code}: {response.text}"
    )
