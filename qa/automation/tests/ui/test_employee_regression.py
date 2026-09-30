"""UI regression suite for Module 05 — Employee directory.

Automated from qa/05-employee/cases/01-employee-list.yaml and 12-authz-tenant-security.yaml. These
add to the existing Employee UI smoke batch: the Status toolbar filter drives a real filtered request,
and the Employee pages never put a tenant id on the wire (the server resolves it from the host/token).

Signed in as the QA admin via `ui_signed_in`; no pre-existing employee is read for its values, edited
or deleted.
"""

from __future__ import annotations

import allure
import pytest

from pages.employees_list_page import EmployeesListPage
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.ui, pytest.mark.regression]


@allure.story("Employee directory")
@allure.title("EMP-LIST-006 — the Status toolbar filter issues a filtered request and reloads without error")
@qa_cases("EMP-LIST-006")
def test_status_filter_drives_a_filtered_request(page, settings, ui_signed_in):
    host = ui_signed_in
    list_page = EmployeesListPage(page).open(settings.ui_url(host, "/employees"))

    option_values = [v for v in list_page.status_filter_option_values() if v]
    if not option_values:
        pytest.skip("The Status filter exposes no concrete status option in this build.")

    filtered_requests: list[str] = []
    page.on(
        "request",
        lambda req: filtered_requests.append(req.url)
        if "/api/employees" in req.url and "status=" in req.url
        else None,
    )

    list_page.filter_status_value(option_values[0])

    assert filtered_requests, "selecting a status did not issue a GET /api/employees?status=... request"
    assert list_page.is_loaded(), "the list did not render (table or empty state) after filtering"


@allure.story("Authorization")
@allure.title("EMP-SEC-010 — the Employee list page never puts a tenant id on the wire")
@qa_cases("EMP-SEC-010")
def test_employee_page_never_sends_a_tenant_id(page, settings, ui_signed_in):
    host = ui_signed_in

    offending: list[str] = []

    def _inspect(req) -> None:
        if "/api/" not in req.url:
            return
        url_l = req.url.lower()
        if "tenantid=" in url_l or "tenant_id=" in url_l:
            offending.append(req.url)
        if req.method in ("POST", "PUT") and req.post_data:
            body = req.post_data.lower()
            if '"tenantid"' in body or '"tenant_id"' in body:
                offending.append(f"{req.method} {req.url} (body carried a tenant id)")

    page.on("request", _inspect)

    list_page = EmployeesListPage(page).open(settings.ui_url(host, "/employees"))
    list_page.search("QAAUTO-DOES-NOT-EXIST-00000000")

    assert not offending, f"Employee page requests carried a tenant id: {offending}"
