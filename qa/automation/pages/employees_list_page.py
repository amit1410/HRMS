"""Page Object for the employee directory (Frontend/HRMS.Web/src/pages/employees/EmployeesPage.tsx)."""

from __future__ import annotations

import allure

from .base_page import BasePage


class EmployeesListPage(BasePage):
    SEARCH_INPUT = "#list-search"
    NEW_EMPLOYEE_LINK = "a.button-primary:has-text('New employee')"
    TABLE = "table.data-table"
    LOADING = ".table-loading"
    EMPTY_STATE_TITLE = ".state-title"
    ROW_NAME_LINK = "a.cell-primary"
    STATUS_FILTER = "label.toolbar-filter:has-text('Status') select"

    @allure.step("Open employees list: {url}")
    def open(self, url: str) -> "EmployeesListPage":
        self.goto(url)
        self.page.wait_for_selector(f"{self.TABLE}, {self.EMPTY_STATE_TITLE}", timeout=15_000)
        return self

    def is_loaded(self) -> bool:
        return self.page.locator(self.SEARCH_INPUT).count() > 0 and (
            self.page.locator(self.TABLE).count() > 0 or self.page.locator(self.EMPTY_STATE_TITLE).count() > 0
        )

    @allure.step("Search employees for '{text}'")
    def search(self, text: str) -> "EmployeesListPage":
        self.page.locator(self.SEARCH_INPUT).fill(text)
        # Search is debounced client-side (useListQuery, 300ms) before the request fires.
        self.page.wait_for_timeout(500)
        self.page.locator(self.LOADING).wait_for(state="hidden", timeout=15_000)
        self.page.wait_for_selector(f"{self.TABLE}, {self.EMPTY_STATE_TITLE}", timeout=15_000)
        return self

    def row_link(self, text: str):
        return self.page.locator(self.ROW_NAME_LINK, has_text=text)

    def has_row(self, text: str) -> bool:
        return self.row_link(text).count() > 0

    def is_empty_state_shown(self) -> bool:
        return self.page.locator(self.EMPTY_STATE_TITLE).count() > 0

    def empty_state_title(self) -> str | None:
        locator = self.page.locator(self.EMPTY_STATE_TITLE)
        return locator.first.inner_text() if locator.count() > 0 else None
