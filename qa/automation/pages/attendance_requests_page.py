"""Page Object for the self-service Regularization/On Duty submission screen, plus the manager
approvals section it conditionally renders (Frontend/HRMS.Web/src/pages/attendance/
AttendanceRequestsPage.tsx), route `/attendance/requests`.

Locators are pinned to the component's `aria-label`s and static class names. The Regularization/
On Duty forms render unconditionally (they don't wait on any API call before appearing); only the
"Manager approvals" card is conditional, on `can(Permissions.attendance.regularizationApprove)` /
`can(Permissions.attendance.onDutyApprove)` from the caller's own token — never assume it is
present. The route itself is wrapped in `<RequirePermission permission={Permissions.attendance.
view}>` + `<RequireEmployeeIdentity>` (App.tsx), same as My Attendance.
"""

from __future__ import annotations

import allure

from .base_page import BasePage


class AttendanceRequestsPage(BasePage):
    REGULARIZATION_DATE_INPUT = "input[aria-label='Regularization date']"
    REGULARIZATION_OUT_INPUT = "input[aria-label='Corrected out']"
    REGULARIZATION_REASON_INPUT = "input[aria-label='Regularization reason']"
    SUBMIT_REGULARIZATION_BUTTON = "button:has-text('Submit Regularization')"

    ON_DUTY_START_INPUT = "input[aria-label='On Duty start date']"
    ON_DUTY_END_INPUT = "input[aria-label='On Duty end date']"
    ON_DUTY_REASON_INPUT = "input[aria-label='On Duty reason']"
    SUBMIT_ON_DUTY_BUTTON = "button:has-text('Submit On Duty')"

    MANAGER_APPROVALS_CARD = ".card:has-text('Manager approvals')"
    ERROR_NOTICE = ".notice-error, [class*='notice'][class*='error']"

    @allure.step("Open Attendance Requests: {url}")
    def open(self, url: str) -> "AttendanceRequestsPage":
        self.goto(url)
        self.page.wait_for_selector(self.SUBMIT_REGULARIZATION_BUTTON, timeout=15_000)
        return self

    def is_loaded(self) -> bool:
        return (
            self.page.locator(self.SUBMIT_REGULARIZATION_BUTTON).count() > 0
            and self.page.locator(self.SUBMIT_ON_DUTY_BUTTON).count() > 0
        )

    def has_manager_approvals_section(self) -> bool:
        return self.page.locator(self.MANAGER_APPROVALS_CARD).count() > 0

    @allure.step("Fill the Regularization form (no submit)")
    def fill_regularization(self, *, date: str, reason: str, corrected_out: str = "") -> None:
        self.page.locator(self.REGULARIZATION_DATE_INPUT).fill(date)
        self.page.locator(self.REGULARIZATION_REASON_INPUT).fill(reason)
        if corrected_out:
            self.page.locator(self.REGULARIZATION_OUT_INPUT).fill(corrected_out)
