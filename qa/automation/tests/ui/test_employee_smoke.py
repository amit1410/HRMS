"""UI smoke suite for Module 05 — Employee Management (Phase 3, first batch).

Proof-of-concept coverage, automated from qa/05-employee/cases/*.yaml. Deliberately narrow — see
qa/automation/README.md "Employee smoke suite (Phase 3)" for what is and isn't covered yet.

Requires QA_TENANT_A_HOST and QA_A_ADMIN_USERNAME/QA_A_ADMIN_PASSWORD (a caller with at least
Employee.View + Employee.Create + Employee.Edit + Employee.Delete). Missing/failing credentials
skip — never fail or fabricate — via the `ui_signed_in` / `admin_api_client` fixtures.

Every employee this suite creates is prefixed with `data.test_data.TEST_DATA_MARKER` and deleted
in fixture teardown; no pre-existing employee is ever read for its values, only searched for by a
marker this run itself generated, and none is ever edited or deleted.
"""

from __future__ import annotations

import allure
import pytest

from core.employee_api import delete_employee, list_employees
from data.test_data import today_iso, unique_last_name
from pages.employee_form_page import EmployeeFormPage
from pages.employees_list_page import EmployeesListPage

pytestmark = [allure.feature("Employee Management"), pytest.mark.ui]


@allure.story("Employee directory")
@allure.title("Employee list page loads")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_list_page_loads(page, settings, ui_signed_in):
    host = ui_signed_in
    list_page = EmployeesListPage(page).open(settings.ui_url(host, "/employees"))

    assert list_page.is_loaded(), "Expected the search box and either a table or an empty state"


@allure.story("Employee directory")
@allure.title("EMP-LIST-004 — search finds an exact match and an unmatched term shows the empty state")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_search_filters_results(page, settings, ui_signed_in, created_employee):
    host = ui_signed_in
    marker = created_employee["lastName"]

    list_page = EmployeesListPage(page).open(settings.ui_url(host, "/employees"))
    list_page.search(marker)
    assert list_page.has_row(marker), f"Expected a row for the just-created employee ({marker!r}) after searching for it"

    list_page.search("QAAUTO-DOES-NOT-EXIST-00000000")
    assert list_page.is_empty_state_shown(), "Expected the empty state for a search term matching nobody"


@allure.story("Employee directory")
@allure.title("Opening an employee's details shows its own Personal Details")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_open_employee_details(page, settings, ui_signed_in, created_employee):
    host = ui_signed_in
    form = EmployeeFormPage(page).open(settings.ui_url(host, f"/employees/{created_employee['id']}"))

    assert form.first_name_value() == created_employee["firstName"]
    assert form.last_name_value() == created_employee["lastName"]


@allure.story("Employee create")
@allure.title("EMP-CREATE-005 / EMP-FE-002 — creating with only the mandatory fields assigns an employee code")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_create_employee_minimum_valid_data(page, settings, ui_signed_in, admin_api_client):
    host = ui_signed_in
    last_name = unique_last_name()
    form = EmployeeFormPage(page).open(settings.ui_url(host, "/employees/new"))
    form.fill_minimum_required("QaAuto", last_name, today_iso())
    form.submit()

    with allure.step("Expect a success message and an assigned employee code"):
        page.wait_for_selector(EmployeeFormPage.SUCCESS_ALERT, timeout=10_000)
        assert form.success_message()
        code = form.employee_code_text()
        assert code, "Expected an employee code to replace the 'New Hire' placeholder after save"

    with allure.step("Clean up: locate the created record by its unique last name and delete it"):
        found = list_employees(admin_api_client, search=last_name).json()["data"]["items"]
        assert found, f"Setup/cleanup problem: could not find the just-created employee via search({last_name!r})"
        delete_employee(admin_api_client, found[0]["id"])


@allure.story("Employee create")
@allure.title("EMP-FE-004 / EMP-PD-001 — missing required fields are rejected with inline errors, nothing created")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_create_employee_missing_required_fields_shows_validation(page, settings, ui_signed_in):
    host = ui_signed_in
    form = EmployeeFormPage(page).open(settings.ui_url(host, "/employees/new"))
    form.clear_required_fields()
    form.submit()

    with allure.step("Expect inline First/Last name errors, no success message, and no navigation away"):
        page.wait_for_selector(f"{EmployeeFormPage.FIRST_NAME_ERROR}, {EmployeeFormPage.LAST_NAME_ERROR}", timeout=10_000)
        assert form.has_field_errors()
        assert form.success_message() is None
        assert "/employees/new" in page.url


@allure.story("Employee edit")
@allure.title("Editing and saving basic Personal Details persists the change")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_edit_basic_employee_personal_details(page, settings, ui_signed_in, created_employee):
    host = ui_signed_in
    new_last_name = unique_last_name(prefix="QAAUTOEDIT")

    form = EmployeeFormPage(page).open(settings.ui_url(host, f"/employees/{created_employee['id']}"))
    form.page.locator(EmployeeFormPage.LAST_NAME).fill(new_last_name)
    form.submit()

    with allure.step("Expect a success message and the change to persist across a reload"):
        page.wait_for_selector(EmployeeFormPage.SUCCESS_ALERT, timeout=10_000)
        assert form.success_message()
        page.reload()
        page.wait_for_selector(EmployeeFormPage.LAST_NAME, timeout=10_000)
        assert form.last_name_value() == new_last_name
