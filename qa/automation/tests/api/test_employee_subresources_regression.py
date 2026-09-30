"""API regression suite for Module 05 — Employee sub-resources.

Automated from qa/05-employee/cases/14-other-subresources.yaml: Family, Education, Previous
Employment, Additional Info and the Audit Log. Covers required/optional field validation, the nominee
boundary, the (deliberately un-aggregated) nominee-percentage behaviour, the single-row upsert for
additional info, audit-log paging, that a mutating action writes an audit row, and tenant isolation.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_education,
    create_family,
    create_previous_employment,
    delete_education,
    delete_family,
    get_additional_info,
    get_audit_log,
    get_education,
    get_family,
    get_previous_employment,
    update_education,
    upsert_additional_info,
    upsert_contact,
)
from data.test_data import unique_employee_email
from utils.allure_evidence import cr_refs, qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


@allure.story("Sub-resources: Family")
@allure.title("EMP-SUB-001 — family required fields and the nominee-percentage boundary")
@qa_cases("EMP-SUB-001")
def test_family_required_and_nominee_boundary(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("first name omitted => 400"):
        assert create_family(admin_api_client, employee_id, lastName="Doe", relationship="Spouse").status_code == 400
    with allure.step("relationship omitted => 400"):
        assert create_family(admin_api_client, employee_id, firstName="Jane", lastName="Doe").status_code == 400
    with allure.step("nominee with 0% => 400 (below 0.01 minimum)"):
        assert create_family(admin_api_client, employee_id, firstName="Jane", lastName="Doe", relationship="Spouse", isNominee=True, nomineePercentage=0).status_code == 400
    with allure.step("nominee with 100% => 201 (upper boundary)"):
        assert create_family(admin_api_client, employee_id, firstName="Jane", lastName="Doe", relationship="Spouse", isNominee=True, nomineePercentage=100).status_code == 201
    with allure.step("non-nominee with no percentage => 201"):
        assert create_family(admin_api_client, employee_id, firstName="Bob", lastName="Doe", relationship="Child", isNominee=False).status_code == 201


@allure.story("Sub-resources: Family")
@allure.title("EMP-SUB-002 — nominee percentages are not summed across family records (CR-60, current behaviour)")
@qa_cases("EMP-SUB-002")
@cr_refs("CR-60")
def test_nominee_percentages_not_aggregated(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    a = create_family(admin_api_client, employee_id, firstName="A", lastName="Nominee", relationship="Spouse", isNominee=True, nomineePercentage=100)
    b = create_family(admin_api_client, employee_id, firstName="B", lastName="Nominee", relationship="Child", isNominee=True, nomineePercentage=100)
    assert a.status_code == 201, a.text
    assert b.status_code == 201, b.text  # combined 200% is accepted — no aggregate check exists


@allure.story("Sub-resources: Education")
@allure.title("EMP-SUB-003 — education: qualification required, rest free-form; update and delete work")
@qa_cases("EMP-SUB-003")
def test_education_crud(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("qualification omitted => 400"):
        assert create_education(admin_api_client, employee_id, institute="Somewhere").status_code == 400
    with allure.step("qualification only => 201"):
        created = create_education(admin_api_client, employee_id, qualification="B.Tech")
        assert created.status_code == 201, created.text
        education_id = created.json()["data"]["id"]
    with allure.step("update the score field => 200"):
        assert update_education(admin_api_client, employee_id, education_id, qualification="B.Tech", score="8.1").status_code == 200
    with allure.step("delete => 200 and the record is gone"):
        assert delete_education(admin_api_client, employee_id, education_id).status_code == 200
        remaining = get_education(admin_api_client, employee_id).json()["data"]
        assert all(e["id"] != education_id for e in remaining)


@allure.story("Sub-resources: Previous Employment")
@allure.title("EMP-SUB-004 — previous employment: company required, other fields optional")
@qa_cases("EMP-SUB-004")
def test_previous_employment_required(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("company omitted => 400"):
        assert create_previous_employment(admin_api_client, employee_id, designation="Engineer").status_code == 400
    with allure.step("company only => 201"):
        assert create_previous_employment(admin_api_client, employee_id, company="Acme Corp").status_code == 201


@allure.story("Sub-resources: Additional Info")
@allure.title("EMP-SUB-005 — additional info is a single upserted row per employee")
@qa_cases("EMP-SUB-005")
def test_additional_info_single_row_upsert(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    assert upsert_additional_info(admin_api_client, employee_id, division="Corporate", paPsa="PA-001").status_code == 200
    first = get_additional_info(admin_api_client, employee_id).json()["data"]
    assert first["division"] == "Corporate"
    assert upsert_additional_info(admin_api_client, employee_id, division="Retail", paPsa="PA-002").status_code == 200
    second = get_additional_info(admin_api_client, employee_id).json()["data"]
    assert second["division"] == "Retail", "the same additional-info row should have been updated"


@allure.story("Sub-resources: Audit Log")
@allure.title("EMP-SUB-007 — a mutating sub-resource action writes a readable audit-log entry")
@qa_cases("EMP-SUB-007")
def test_audit_log_records_mutation(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    before = get_audit_log(admin_api_client, employee_id).json()["data"]["totalCount"]
    assert upsert_contact(admin_api_client, employee_id, officialEmail=unique_employee_email()).status_code == 200
    after = get_audit_log(admin_api_client, employee_id).json()["data"]
    assert after["totalCount"] > before, "the contact change did not produce an audit-log entry"


@allure.story("Sub-resources: Audit Log")
@allure.title("EMP-SUB-010 — an out-of-range audit-log page returns an empty page, not an error")
@qa_cases("EMP-SUB-010")
def test_audit_log_out_of_range_page(admin_api_client, created_employee):
    response = get_audit_log(admin_api_client, created_employee["id"], page=999)
    assert response.status_code == 200, response.text
    assert response.json()["data"]["items"] == []


@allure.story("Sub-resources")
@allure.title("EMP-SUB-009 — deleting an unknown sub-resource record id is a 404")
@qa_cases("EMP-SUB-009")
def test_delete_unknown_subresource_is_404(admin_api_client, created_employee):
    assert delete_family(admin_api_client, created_employee["id"], str(uuid.uuid4())).status_code == 404


@allure.story("Sub-resources")
@allure.title("EMP-SUB-008 — every sub-resource on an unknown/cross-tenant employee id is a 404")
@qa_cases("EMP-SUB-008")
def test_subresources_unknown_employee_is_404(admin_api_client):
    unknown = str(uuid.uuid4())
    assert get_family(admin_api_client, unknown).status_code == 404
    assert get_education(admin_api_client, unknown).status_code == 404
    assert get_previous_employment(admin_api_client, unknown).status_code == 404
    assert get_additional_info(admin_api_client, unknown).status_code == 404
