"""Page Object for the self-service leave list (Frontend/HRMS.Web/src/pages/leave/MyLeaveRequestsPage.tsx)."""

from __future__ import annotations

import allure

from .base_page import BasePage


class MyLeaveRequestsPage(BasePage):
    SKELETON = ".my-requests-skeleton"
    ERROR_CARD = ".my-requests-error-card"
    EMPTY_CARD = ".my-requests-empty-card"
    RESULTS_CARD = ".my-requests-results-card"
    TABLE = "table.my-requests-table"
    APPLY_LEAVE_LINK = "a[aria-label='Apply for Leave']"
    STATUS_FILTER = "select[aria-label='Filter requests']"

    @allure.step("Open My Leave Requests: {url}")
    def open(self, url: str) -> "MyLeaveRequestsPage":
        self.goto(url)
        self.page.wait_for_selector(
            f"{self.EMPTY_CARD}, {self.RESULTS_CARD}, {self.ERROR_CARD}", timeout=15_000
        )
        return self

    def is_loaded(self) -> bool:
        return (
            self.page.locator(self.EMPTY_CARD).count() > 0
            or self.page.locator(self.RESULTS_CARD).count() > 0
        )

    def is_error_shown(self) -> bool:
        return self.page.locator(self.ERROR_CARD).count() > 0

    def is_empty_state_shown(self) -> bool:
        return self.page.locator(self.EMPTY_CARD).count() > 0

    def has_results_table(self) -> bool:
        return self.page.locator(self.TABLE).count() > 0
