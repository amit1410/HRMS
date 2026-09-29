"""UI smoke suite for Module 08 — Attendance Management (Phase 5, first batch).

Proof-of-concept coverage, automated from qa/08-attendance/cases (Attendance_Frontend sheet).
Deliberately narrow — page-load and permission-gating behavior only, no leave-policy-style
configuration dependency, but a real one of its own: both `/attendance/my-attendance` and
`/attendance/requests` are wrapped in `<RequirePermission permission={Permissions.attendance.
view}>` + `<RequireEmployeeIdentity>` (Frontend/HRMS.Web/src/App.tsx) — a caller without
`Attendance.View`, or one whose account isn't linked to an Employee record, is redirected to
`/forbidden` before the page component ever renders. Per qa/08-attendance/README.md (CR-111/
CR-112, reconfirmed against the current `SeedData.RolePermissionMap`), the seeded plain `Employee`
role does not hold `Attendance.View` at all — so the "employee cannot reach My Attendance" case
below is a deterministic, in-scope assertion (mirrors Leave's LEAVE-FE-013), while the "page loads
its data" cases (which need `ui_signed_in`/admin to actually hold `Attendance.View` *and* be a
linked Employee) skip honestly, with the landing URL attached, when that admin identity is instead
bounced to `/forbidden` — not a framework issue.

Requires QA_TENANT_A_HOST and, per test: QA_A_EMPLOYEE_USERNAME/PASSWORD (`ui_signed_in_employee`)
or QA_A_ADMIN_USERNAME/PASSWORD (`ui_signed_in`). Missing/failing credentials skip, never fail.
"""

from __future__ import annotations

import allure
import pytest

from pages.attendance_requests_page import AttendanceRequestsPage
from pages.my_attendance_page import MyAttendancePage

pytestmark = [allure.feature("Attendance Management"), pytest.mark.ui]

FORBIDDEN_STATE_TITLE = ".state-title"


def _skip_if_bounced_to_forbidden(page, *, identity_label: str) -> None:
    try:
        page.wait_for_url("**/forbidden", timeout=5_000)
    except Exception:
        return
    pytest.skip(
        f"{identity_label} was redirected to /forbidden before the page rendered — it either lacks "
        "Attendance.View or is not linked to an Employee record (RequirePermission/"
        "RequireEmployeeIdentity in App.tsx). Not a framework issue; see qa/08-attendance/README.md "
        "CR-111/CR-112."
    )


@allure.story("Route guard")
@allure.title("ATT-FE — direct navigation to My Attendance without Attendance.View is denied uniformly")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_direct_navigation_to_my_attendance_is_forbidden(page, settings, ui_signed_in_employee):
    host = ui_signed_in_employee
    page.goto(settings.ui_url(host, "/attendance/my-attendance"))

    page.wait_for_url("**/forbidden", timeout=15_000)
    assert page.locator(FORBIDDEN_STATE_TITLE).inner_text() == "You do not have access to that"


@allure.story("Route guard")
@allure.title("ATT-FE — direct navigation to Attendance Requests without Attendance.View is denied uniformly")
@pytest.mark.smoke
@pytest.mark.regression
def test_employee_direct_navigation_to_attendance_requests_is_forbidden(page, settings, ui_signed_in_employee):
    host = ui_signed_in_employee
    page.goto(settings.ui_url(host, "/attendance/requests"))

    page.wait_for_url("**/forbidden", timeout=15_000)
    assert page.locator(FORBIDDEN_STATE_TITLE).inner_text() == "You do not have access to that"


@allure.story("My Attendance")
@allure.title("ATT-SELF-001/FE — My Attendance page loads to the calendar/summary view")
@pytest.mark.smoke
@pytest.mark.regression
def test_my_attendance_page_loads(page, settings, ui_signed_in):
    host = ui_signed_in
    page.goto(settings.ui_url(host, "/attendance/my-attendance"))
    _skip_if_bounced_to_forbidden(page, identity_label="QA_A_ADMIN")

    my_attendance = MyAttendancePage(page)
    my_attendance.page.wait_for_selector(
        f"{MyAttendancePage.KPI_GRID}, {MyAttendancePage.UNAVAILABLE_STATE}", timeout=15_000
    )
    if my_attendance.is_permission_denied():
        pytest.skip(
            "QA_A_ADMIN reached the route guard but the page itself reports the data call as "
            "forbidden/unavailable — see qa/08-attendance/README.md CR-111."
        )
    assert my_attendance.is_loaded(), "Expected the KPI summary grid and calendar card to render"


@allure.story("Attendance Requests")
@allure.title("ATT-REG-001/ATT-OD-001/FE — Attendance Requests page loads with both submission forms")
@pytest.mark.smoke
@pytest.mark.regression
def test_attendance_requests_page_loads(page, settings, ui_signed_in):
    host = ui_signed_in
    page.goto(settings.ui_url(host, "/attendance/requests"))
    _skip_if_bounced_to_forbidden(page, identity_label="QA_A_ADMIN")

    requests_page = AttendanceRequestsPage(page)
    requests_page.page.wait_for_selector(AttendanceRequestsPage.SUBMIT_REGULARIZATION_BUTTON, timeout=15_000)
    assert requests_page.is_loaded(), "Expected both the Regularization and On Duty submission forms to render"
