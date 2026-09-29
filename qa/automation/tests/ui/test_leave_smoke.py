"""UI smoke suite for Module 07 — Leave Management (Phase 4, first batch).

Proof-of-concept coverage, automated from qa/07-leave/cases/16-frontend.yaml. Deliberately
narrow — see qa/automation/README.md and qa/07-leave/README.md (Leave is a much larger
configuration UI than the pieces this batch exercises; CR-81 means a real Preview/Submit happy
path is not guaranteed to be exercisable through the UI in any given environment, so this batch
sticks to page-load and permission-gating behavior that needs no leave-policy configuration to be
correct).

Requires QA_TENANT_A_HOST and QA_A_EMPLOYEE_USERNAME/QA_A_EMPLOYEE_PASSWORD (a linked employee
holding the plain, self-service `Employee` role — see `ui_signed_in_employee` in conftest.py).
Missing/failing credentials skip, never fail or fabricate.
"""

from __future__ import annotations

import allure
import pytest

from pages.my_leave_requests_page import MyLeaveRequestsPage

pytestmark = [allure.feature("Leave Management"), pytest.mark.ui]


@allure.story("My Leave Requests")
@allure.title("LEAVE-FE-001 — My Leave Requests page loads to either the results table or the empty state")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_my_leave_requests_page_loads(page, settings, ui_signed_in_employee):
    host = ui_signed_in_employee
    my_requests = MyLeaveRequestsPage(page).open(settings.ui_url(host, "/leave-management/my-requests"))

    assert not my_requests.is_error_shown(), "Did not expect the list API call to fail"
    assert my_requests.is_loaded(), "Expected either the results table or the empty-state card"


@allure.story("Route guard")
@allure.title("LEAVE-FE-013 — direct navigation to an admin Leave route without permission is denied uniformly")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_direct_navigation_to_leave_policies_is_forbidden(page, settings, ui_signed_in_employee):
    host = ui_signed_in_employee
    page.goto(settings.ui_url(host, "/leave-management/policies"))

    page.wait_for_url("**/forbidden", timeout=15_000)
    assert page.locator(".state-title").inner_text() == "You do not have access to that"
