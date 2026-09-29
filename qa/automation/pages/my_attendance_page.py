"""Page Object for the self-service attendance calendar/summary
(Frontend/HRMS.Web/src/pages/attendance/MyAttendancePage.tsx), route `/attendance/my-attendance`.

The route is wrapped in `<RequirePermission permission={Permissions.attendance.view}>` +
`<RequireEmployeeIdentity>` (App.tsx) — a caller without `Attendance.View` never reaches this
component at all, it is redirected to `/forbidden` first (see `test_employee_...forbidden` in
`tests/ui/test_attendance_smoke.py`, mirroring Leave's LEAVE-FE-013). This Page Object only
describes what the component itself renders once that guard has already let the caller through.
"""

from __future__ import annotations

import allure

from .base_page import BasePage


class MyAttendancePage(BasePage):
    KPI_GRID = ".my-attendance-kpi-grid"
    CALENDAR_CARD = ".my-attendance-calendar-card"
    TABLE = "table.my-attendance-table"
    EMPTY_STATE = ".my-attendance-empty"
    UNAVAILABLE_STATE = ".my-attendance-unavailable"
    FORBIDDEN_ALERT = ".my-attendance-alert"

    @allure.step("Open My Attendance: {url}")
    def open(self, url: str) -> "MyAttendancePage":
        self.goto(url)
        self.page.wait_for_selector(self.KPI_GRID, timeout=15_000)
        return self

    def is_loaded(self) -> bool:
        return self.page.locator(self.KPI_GRID).count() > 0 and self.page.locator(self.CALENDAR_CARD).count() > 0

    def is_permission_denied(self) -> bool:
        """True when the component itself renders the "you do not have permission" state — a
        runtime denial distinct from the route-level `/forbidden` redirect (see module docstring)."""
        return self.page.locator(self.UNAVAILABLE_STATE).count() > 0 or self.page.locator(self.FORBIDDEN_ALERT).count() > 0

    def has_results_table(self) -> bool:
        return self.page.locator(self.TABLE).count() > 0

    def is_empty_state_shown(self) -> bool:
        return self.page.locator(self.EMPTY_STATE).count() > 0
