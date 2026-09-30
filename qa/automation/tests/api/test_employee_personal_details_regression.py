"""API regression suite for Module 05 — Employee Personal Details.

Automated from qa/05-employee/cases/03-personal-details.yaml. Covers name/date/statutory validation,
sensitive-value masking on ordinary reads, and the blank-preserves-stored-value rule. Each test uses
a disposable employee it creates and deletes; sensitive assertions confirm the raw value never
appears on the masked read.

The permission-split cases (EMP-PD-008/009) need an Employee.View-without-EmployeeSensitive.View
caller, which this environment does not provision, so they are out of this batch.
"""

from __future__ import annotations

import datetime
import uuid

import allure
import pytest

from core.employee_api import (
    create_personal_details,
    delete_employee,
    get_employee,
    get_sensitive_details,
    update_personal_details,
)
from data.test_data import today_iso, unique_last_name
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]

TODAY = datetime.date.today()


def _pd(**overrides):
    body = dict(firstName="QaAuto", lastName=unique_last_name(), dateOfJoining=today_iso())
    body.update(overrides)
    return body


@allure.story("Personal details")
@allure.title("EMP-PD-001 / EMP-PD-012 — first/last name required, trimmed, max 100 chars")
@qa_cases("EMP-PD-001", "EMP-PD-012")
def test_name_required_and_length(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("empty first name => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(firstName="")).status_code == 400
    with allure.step("whitespace-only last name => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(lastName="   ")).status_code == 400
    with allure.step("first name 101 chars => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(firstName="A" * 101)).status_code == 400
    with allure.step("first name exactly 100 chars => 200"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(firstName="A" * 100)).status_code == 200
    with allure.step("whitespace-only first name on create => 400"):
        r = create_personal_details(admin_api_client, firstName="   ", lastName=unique_last_name(), dateOfJoining=today_iso())
        assert r.status_code == 400, r.text


@allure.story("Personal details")
@allure.title("EMP-PD-003 — date of joining is required and bounded (<=1yr future, <=100yr past)")
@qa_cases("EMP-PD-003")
def test_date_of_joining_bounds(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("omitted (default) => 400"):
        assert update_personal_details(admin_api_client, employee_id, firstName="QaAuto", lastName=unique_last_name()).status_code == 400
    with allure.step("more than 1 year in the future => 400"):
        future = (TODAY + datetime.timedelta(days=366 + 1)).isoformat()
        assert update_personal_details(admin_api_client, employee_id, **_pd(dateOfJoining=future)).status_code == 400
    with allure.step("~1 year in the future (within bound) => 200"):
        within = (TODAY + datetime.timedelta(days=364)).isoformat()
        assert update_personal_details(admin_api_client, employee_id, **_pd(dateOfJoining=within)).status_code == 200
    with allure.step("101 years in the past => 400"):
        far_past = TODAY.replace(year=TODAY.year - 101).isoformat()
        assert update_personal_details(admin_api_client, employee_id, **_pd(dateOfJoining=far_past)).status_code == 400


@allure.story("Personal details")
@allure.title("EMP-PD-004 — date of birth must be past, plausible, and >=14 years before joining")
@qa_cases("EMP-PD-004")
def test_date_of_birth_bounds_and_age(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    joining = "2020-06-15"
    with allure.step("DOB today (not in the past) => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(dateOfJoining=joining, dateOfBirth=today_iso())).status_code == 400
    with allure.step("DOB 121 years ago => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(dateOfJoining=joining, dateOfBirth=TODAY.replace(year=TODAY.year - 121).isoformat())).status_code == 400
    with allure.step("13 years old on joining => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(dateOfJoining=joining, dateOfBirth="2007-06-15")).status_code == 400
    with allure.step("exactly 14 on joining => 200"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(dateOfJoining=joining, dateOfBirth="2006-06-15")).status_code == 200


@allure.story("Personal details")
@allure.title("EMP-PD-005 — Aadhaar/PAN/UAN format validated when supplied, all optional")
@qa_cases("EMP-PD-005")
def test_statutory_id_formats(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("5-digit Aadhaar => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(aadhaarNumber="12345")).status_code == 400
    with allure.step("12-digit Aadhaar => 200"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(aadhaarNumber="123456789012")).status_code == 200
    with allure.step("malformed PAN => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(panNumber="ABCDE12345")).status_code == 400
    with allure.step("valid PAN => 200"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(panNumber="ABCDE1234F")).status_code == 200
    with allure.step("10-digit UAN => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(uanNumber="1234567890")).status_code == 400
    with allure.step("all omitted => 200"):
        assert update_personal_details(admin_api_client, employee_id, **_pd()).status_code == 200


@allure.story("Personal details")
@allure.title("EMP-PD-006 — ESIC number required only when ESIC is applicable")
@qa_cases("EMP-PD-006")
def test_esic_number_conditional(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("esicApplicable=true, no number => 400"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(esicApplicable=True)).status_code == 400
    with allure.step("esicApplicable=true, number supplied => 200"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(esicApplicable=True, esicNumber="ESIC-001")).status_code == 200
    with allure.step("esicApplicable=false, no number => 200"):
        assert update_personal_details(admin_api_client, employee_id, **_pd(esicApplicable=False)).status_code == 200


@allure.story("Personal details")
@allure.title("EMP-PD-007 — sensitive ids are masked on read; a blank submission preserves the stored value")
@qa_cases("EMP-PD-007")
def test_masking_and_blank_preserves_stored(admin_api_client):
    created = create_personal_details(
        admin_api_client, firstName="QaAuto", lastName=unique_last_name(), dateOfJoining=today_iso(),
        aadhaarNumber="123456789012", panNumber="ABCDE1234F",
    )
    assert created.status_code == 201, created.text
    employee_id = created.json()["data"]["id"]
    try:
        with allure.step("ordinary read is masked, never the raw value"):
            detail = get_employee(admin_api_client, employee_id).json()["data"]
            assert detail.get("maskedAadhaarNumber") and detail["maskedAadhaarNumber"] != "123456789012"
            assert detail.get("maskedPanNumber") and detail["maskedPanNumber"] != "ABCDE1234F"
            assert "123456789012" not in str(detail), "raw Aadhaar leaked into the ordinary detail read"
        with allure.step("blank Aadhaar + omitted PAN on update is accepted"):
            upd = update_personal_details(admin_api_client, employee_id, firstName="QaAuto", lastName=detail["lastName"], dateOfJoining=today_iso(), aadhaarNumber="")
            assert upd.status_code == 200, upd.text
        with allure.step("the previously-stored raw values are unchanged (blank did not clear them)"):
            sensitive = get_sensitive_details(admin_api_client, employee_id).json()["data"]
            assert sensitive["aadhaarNumber"] == "123456789012"
            assert sensitive["panNumber"] == "ABCDE1234F"
    finally:
        delete_employee(admin_api_client, employee_id)


@allure.story("Personal details")
@allure.title("EMP-PD-011 — Unicode names round-trip unchanged")
@qa_cases("EMP-PD-011")
def test_unicode_names_round_trip(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    upd = update_personal_details(admin_api_client, employee_id, firstName="Renée", lastName="田中", dateOfJoining=today_iso())
    assert upd.status_code == 200, upd.text
    detail = get_employee(admin_api_client, employee_id).json()["data"]
    assert detail["firstName"] == "Renée"
    assert detail["lastName"] == "田中"


@allure.story("Personal details")
@allure.title("EMP-PD-010 — a personal-details update leaves organizational assignments untouched")
@qa_cases("EMP-PD-010")
def test_personal_update_touches_only_personal_fields(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    before = get_employee(admin_api_client, employee_id).json()["data"]
    upd = update_personal_details(admin_api_client, employee_id, firstName="QaAutoChanged", lastName=before["lastName"], dateOfJoining=today_iso())
    assert upd.status_code == 200, upd.text
    after = get_employee(admin_api_client, employee_id).json()["data"]
    assert after["firstName"] == "QaAutoChanged", "the personal field we changed should have taken effect"
    for field in ("departmentId", "designationId", "reportingManagerId"):
        assert after.get(field) == before.get(field), f"{field} changed during a personal-details-only update"


@allure.story("Personal details")
@allure.title("EMP-PD-013 — personal-details PUT on an unknown/cross-tenant id is a 404")
@qa_cases("EMP-PD-013")
def test_personal_details_unknown_id_is_404(admin_api_client):
    # A cross-tenant id is reported identically to a fabricated one (single QA tenant here).
    assert update_personal_details(admin_api_client, str(uuid.uuid4()), **_pd()).status_code == 404
