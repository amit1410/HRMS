"""API regression suite for Module 05 — Employee Employment (joining / contractual, 1:1 per employee).

Automated from qa/05-employee/cases/06-employment.yaml. Covers the upsert round-trip, the joining/
first-hired/group/confirmation date-order rules, probation/notice period+unit rules, notice-period
date rules, referrer validation, and tenant isolation. Requires EmploymentHistory.View/.Change (the
QA admin holds both).
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_personal_details,
    delete_employee,
    get_employment,
    upsert_employment,
)
from data.test_data import today_iso, unique_last_name
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


def _emp(**overrides):
    body = dict(firstHiredDate="2020-01-01", dateOfJoining="2020-06-01", jobStatus="Confirmed")
    body.update(overrides)
    return body


@allure.story("Employment")
@allure.title("EMP-JOIN-001 — employment upserts, reads back, and updates the same 1:1 row")
@qa_cases("EMP-JOIN-001")
def test_employment_upsert_round_trip(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    created = upsert_employment(
        admin_api_client, employee_id,
        **_emp(probationPeriod=90, probationPeriodUnit="Days", noticePeriod=30, noticePeriodUnit="Days"),
    )
    assert created.status_code == 200, created.text
    fetched = get_employment(admin_api_client, employee_id).json()["data"]
    assert fetched["firstHiredDate"] == "2020-01-01"
    assert fetched["jobStatus"] == "Confirmed"
    with allure.step("update jobStatus only => same row updated"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(jobStatus="Probation")).status_code == 200
        assert get_employment(admin_api_client, employee_id).json()["data"]["jobStatus"] == "Probation"


@allure.story("Employment")
@allure.title("EMP-JOIN-002 — joining/first-hired/group/confirmation date-order rules are enforced")
@qa_cases("EMP-JOIN-002")
def test_employment_date_order_rules(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("dateOfJoining before firstHiredDate => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(dateOfJoining="2019-01-01")).status_code == 400
    with allure.step("groupDateOfJoining after dateOfJoining => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(groupDateOfJoining="2021-01-01")).status_code == 400
    with allure.step("confirmationDate before dateOfJoining => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(confirmationDate="2019-01-01")).status_code == 400
    with allure.step("all dates in valid order => 200"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(groupDateOfJoining="2020-01-01", confirmationDate="2020-12-01")).status_code == 200


@allure.story("Employment")
@allure.title("EMP-JOIN-003 — probation/notice period+unit rules are enforced")
@qa_cases("EMP-JOIN-003")
def test_probation_and_notice_units(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("probation period without a unit => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(probationPeriod=90)).status_code == 400
    with allure.step("notice unit 'Years' (not allowed for notice) => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(noticePeriod=1, noticePeriodUnit="Years")).status_code == 400
    with allure.step("notice period 30 + unit 'Days' => 200"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(noticePeriod=30, noticePeriodUnit="Days")).status_code == 200


@allure.story("Employment")
@allure.title("EMP-JOIN-004 — notice start required while serving; notice end cannot precede start")
@qa_cases("EMP-JOIN-004")
def test_notice_period_date_rules(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("noticeStatus=Active without a start date => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(noticeStatus="Active")).status_code == 400
    with allure.step("notice end before notice start => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(noticeStatus="Active", noticeStartDate="2024-06-01", noticeEndDate="2024-05-01")).status_code == 400
    with allure.step("notice status Active with valid start+end => 200"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(noticeStatus="Active", noticeStartDate="2024-06-01", noticeEndDate="2024-07-01")).status_code == 200


@allure.story("Employment")
@allure.title("EMP-JOIN-005 — referrer must exist, be Active, same-tenant, and not the employee themselves")
@qa_cases("EMP-JOIN-005")
def test_referrer_validation(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("self-referral => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(referredByEmployeeId=employee_id)).status_code == 400
    with allure.step("nonexistent/cross-tenant referrer => 400"):
        assert upsert_employment(admin_api_client, employee_id, **_emp(referredByEmployeeId=str(uuid.uuid4()))).status_code == 400
    referrer = create_personal_details(admin_api_client, firstName="QaAuto", lastName=unique_last_name(prefix="QAAUTOREF"), dateOfJoining=today_iso())
    assert referrer.status_code == 201, referrer.text
    referrer_id = referrer.json()["data"]["id"]
    try:
        with allure.step("a valid active same-tenant referrer => 200"):
            assert upsert_employment(admin_api_client, employee_id, **_emp(referredByEmployeeId=referrer_id)).status_code == 200
    finally:
        delete_employee(admin_api_client, referrer_id)


@allure.story("Employment")
@allure.title("EMP-JOIN-007 — employment on an unknown/cross-tenant employee id is a 404")
@qa_cases("EMP-JOIN-007")
def test_employment_unknown_employee_is_404(admin_api_client):
    unknown = uuid.uuid4()
    assert get_employment(admin_api_client, str(unknown)).status_code == 404
    assert upsert_employment(admin_api_client, str(unknown), **_emp()).status_code == 404
