"""Page Object for the tenant sign-in screen (Frontend/HRMS.Web/src/pages/LoginPage.tsx).

Locators are pinned to stable attributes from that component (data-testid, input ids, static
class names) rather than visible text, which varies by tenant branding.
"""

from __future__ import annotations

import allure

from .base_page import BasePage


class LoginPage(BasePage):
    ROOT = '[data-testid="login-page"]'
    IDENTIFIER_INPUT = "#identifier"
    PASSWORD_INPUT = "#password"
    SUBMIT_BUTTON = ".login-submit"
    FORM_ERROR = ".login-error"
    IDENTIFIER_FIELD_ERROR = "#identifier-error"
    PASSWORD_FIELD_ERROR = "#password-error"
    REMEMBER_ME_CHECKBOX = 'input[name="rememberMe"]'
    # Rendered instead of the form when the address resolves to no organization, or branding
    # otherwise can't load — see the `Unavailable` component in LoginPage.tsx.
    UNAVAILABLE_TITLE = ".login-title"

    @allure.step("Open login page: {url}")
    def open(self, url: str) -> "LoginPage":
        self.goto(url)
        # The page renders a spinner while branding loads, then either the form (ROOT) or the
        # "workspace not found/unavailable" state (UNAVAILABLE_TITLE) — wait for whichever settles.
        self.page.wait_for_selector(f"{self.ROOT}, {self.UNAVAILABLE_TITLE}", timeout=10_000)
        return self

    def is_loaded(self) -> bool:
        return self.page.locator(self.ROOT).count() > 0

    def is_workspace_unavailable(self) -> bool:
        return self.page.locator(self.UNAVAILABLE_TITLE).count() > 0

    @allure.step("Fill and submit the login form")
    def login(self, identifier: str, password: str, *, remember_me: bool = False) -> None:
        self.page.locator(self.IDENTIFIER_INPUT).fill(identifier)
        self.page.locator(self.PASSWORD_INPUT).fill(password)
        checkbox = self.page.locator(self.REMEMBER_ME_CHECKBOX)
        if remember_me:
            checkbox.check()
        else:
            checkbox.uncheck()
        self.page.locator(self.SUBMIT_BUTTON).click()

    def error_message(self) -> str | None:
        locator = self.page.locator(self.FORM_ERROR)
        if locator.count() == 0:
            return None
        return locator.first.inner_text()
