"""Reusable login/session helpers shared by API and UI tests."""

from __future__ import annotations

import allure

from core.api_client import ApiClient
from pages.login_page import LoginPage


@allure.step("API login as {identifier}")
def api_login(client: ApiClient, identifier: str, password: str):
    return client.post("/api/auth/login", json={"identifier": identifier, "password": password})


@allure.step("API logout")
def api_logout(client: ApiClient, refresh_token: str, access_token: str):
    return client.post(
        "/api/auth/logout",
        json={"refreshToken": refresh_token},
        headers={"Authorization": f"Bearer {access_token}"},
    )


@allure.step("API refresh")
def api_refresh(client: ApiClient, refresh_token: str):
    return client.post("/api/auth/refresh", json={"refreshToken": refresh_token})


@allure.step("UI sign in as {identifier}")
def ui_login(login_page: LoginPage, identifier: str, password: str) -> None:
    login_page.login(identifier, password)
