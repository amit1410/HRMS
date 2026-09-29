"""Page Object for the minimal post-login shell (Frontend/HRMS.Web/src/layout/Header.tsx).

Only what the authentication smoke suite needs: proof a session is active, and the way to end it.
"""

from __future__ import annotations

import allure

from .base_page import BasePage


class DashboardPage(BasePage):
    SIGN_OUT_BUTTON = "button:has-text('Sign out')"

    def is_loaded(self, timeout: float = 10_000) -> bool:
        """True once the signed-in header (Sign out button) is visible.

        A valid login redirects to /dashboard before React has necessarily finished mounting the
        header for the freshly-set `user` — checking `.count()` immediately after the URL changes
        raced that render and produced a false failure. This waits (bounded) for the real signal
        instead of sampling the DOM once.
        """
        try:
            self.page.locator(self.SIGN_OUT_BUTTON).wait_for(state="visible", timeout=timeout)
            return True
        except Exception:
            return False

    @allure.step("Sign out via the workspace header")
    def sign_out(self) -> None:
        self.page.locator(self.SIGN_OUT_BUTTON).click()
