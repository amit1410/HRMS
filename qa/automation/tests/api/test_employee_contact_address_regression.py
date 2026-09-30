"""API regression suite for Module 05 — Employee Contact and Address.

Automated from qa/05-employee/cases/04-contact-address.yaml. Exercises the one-row contact upsert,
email format/uniqueness, the two-address (Current/Permanent) model, free-text address length caps,
and hard-delete-then-re-add. Every record is created against a disposable employee and cleaned up.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_full,
    delete_address,
    delete_employee,
    get_addresses,
    get_contact,
    upsert_address,
    upsert_contact,
)
from data.test_data import today_iso, unique_employee_code, unique_employee_email, unique_last_name
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


def _addresses(response):
    return response.json()["data"]


@allure.story("Contact and address")
@allure.title("EMP-CA-001 — contact is a single upserted row per employee")
@qa_cases("EMP-CA-001")
def test_contact_is_single_upserted_row(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    first = upsert_contact(
        admin_api_client, employee_id,
        officialEmail=unique_employee_email(), officialPhone="+1 555 0100", personalPhone="+1 555 0200",
    )
    assert first.status_code == 200, first.text
    second = upsert_contact(admin_api_client, employee_id, officialPhone="+1 555 9999")
    assert second.status_code == 200, second.text
    fetched = get_contact(admin_api_client, employee_id).json()["data"]
    assert fetched["officialPhone"] == "+1 555 9999", "the same contact row should have been updated in place"


@allure.story("Contact and address")
@allure.title("EMP-CA-002 / EMP-CA-005 — email format and phone length are validated when supplied")
@qa_cases("EMP-CA-002", "EMP-CA-005")
def test_contact_field_validation(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("malformed official email => 400"):
        assert upsert_contact(admin_api_client, employee_id, officialEmail="not-an-email").status_code == 400
    with allure.step("no personal/alternate email => 200 (all optional)"):
        assert upsert_contact(admin_api_client, employee_id, officialPhone="12345").status_code == 200
    with allure.step("31-character phone => 400"):
        assert upsert_contact(admin_api_client, employee_id, officialPhone="9" * 31).status_code == 400


@allure.story("Contact and address")
@allure.title("EMP-CA-003 — official email is unique per tenant (case-insensitive) and mirrors onto the legacy column")
@qa_cases("EMP-CA-003")
def test_official_email_uniqueness_and_mirror(admin_api_client):
    shared_email = unique_employee_email()
    # First employee claims the official email.
    owner = create_full(
        admin_api_client, employeeCode=unique_employee_code(), firstName="QaAuto",
        lastName=unique_last_name(), email=unique_employee_email(), dateOfJoining=today_iso(),
    )
    assert owner.status_code == 201, owner.text
    owner_id = owner.json()["data"]["id"]
    other = create_full(
        admin_api_client, employeeCode=unique_employee_code(), firstName="QaAuto",
        lastName=unique_last_name(), email=unique_employee_email(), dateOfJoining=today_iso(),
    )
    assert other.status_code == 201, other.text
    other_id = other.json()["data"]["id"]
    try:
        assert upsert_contact(admin_api_client, owner_id, officialEmail=shared_email).status_code == 200
        with allure.step("the same email in a different case, for another employee => 409"):
            clash = upsert_contact(admin_api_client, other_id, officialEmail=shared_email.upper())
            assert clash.status_code == 409, clash.text
        with allure.step("a fresh unique official email => 200 and mirrors onto Employee.email"):
            fresh = unique_employee_email()
            assert upsert_contact(admin_api_client, other_id, officialEmail=fresh).status_code == 200
            from core.employee_api import get_employee
            assert get_employee(admin_api_client, other_id).json()["data"]["email"].lower() == fresh.lower()
    finally:
        delete_employee(admin_api_client, owner_id)
        delete_employee(admin_api_client, other_id)


@allure.story("Contact and address")
@allure.title("EMP-CA-008 / EMP-CA-010 — at most Current+Permanent addresses; re-post replaces in place; delete then re-add is fresh")
@qa_cases("EMP-CA-008", "EMP-CA-010")
def test_address_two_row_model_and_delete_readd(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("create Current and Permanent => exactly 2 rows"):
        assert upsert_address(admin_api_client, employee_id, addressType="Current", addressLine1="12 Main St", city="Testville").status_code == 200
        assert upsert_address(admin_api_client, employee_id, addressType="Permanent", addressLine1="9 Other Rd", city="Elsewhere").status_code == 200
        rows = _addresses(get_addresses(admin_api_client, employee_id))
        assert len({r["addressType"] for r in rows}) == 2 and len(rows) == 2, rows
    with allure.step("re-post Current => same row replaced, still 2 rows"):
        assert upsert_address(admin_api_client, employee_id, addressType="Current", addressLine1="12 Main St, Apt 2", city="Testville").status_code == 200
        rows = _addresses(get_addresses(admin_api_client, employee_id))
        assert len(rows) == 2, rows
        current = next(r for r in rows if r["addressType"] == "Current")
        assert current["addressLine1"] == "12 Main St, Apt 2"
    with allure.step("delete Permanent then re-add => fresh row"):
        permanent_id = next(r for r in rows if r["addressType"] == "Permanent")["id"]
        assert delete_address(admin_api_client, employee_id, permanent_id).status_code == 200
        rows = _addresses(get_addresses(admin_api_client, employee_id))
        assert [r["addressType"] for r in rows] == ["Current"], rows
        assert upsert_address(admin_api_client, employee_id, addressType="Permanent", addressLine1="New Rd").status_code == 200
        rows = _addresses(get_addresses(admin_api_client, employee_id))
        assert len(rows) == 2, rows


@allure.story("Contact and address")
@allure.title("EMP-CA-009 — address fields are free text with length caps and no master-list validation")
@qa_cases("EMP-CA-009")
def test_address_free_text_and_length_caps(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("an unreal country is accepted (no master-list check)"):
        assert upsert_address(admin_api_client, employee_id, addressType="Current", country="Narnia").status_code == 200
    with allure.step("city over 100 chars => 400"):
        assert upsert_address(admin_api_client, employee_id, addressType="Current", city="C" * 101).status_code == 400
    with allure.step("zip over 20 chars => 400"):
        assert upsert_address(admin_api_client, employee_id, addressType="Current", zipCode="1" * 21).status_code == 400


@allure.story("Contact and address")
@allure.title("EMP-CA-013 — deleting an unknown or wrong-employee address id is a 404")
@qa_cases("EMP-CA-013")
def test_delete_unknown_address_is_404(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    assert delete_address(admin_api_client, employee_id, str(uuid.uuid4())).status_code == 404


@allure.story("Contact and address")
@allure.title("EMP-CA-007 / EMP-CA-012 — contact/address on an unknown/cross-tenant employee id is a 404")
@qa_cases("EMP-CA-007", "EMP-CA-012")
def test_contact_address_unknown_employee_is_404(admin_api_client):
    unknown = uuid.uuid4()
    assert get_contact(admin_api_client, str(unknown)).status_code == 404
    assert upsert_contact(admin_api_client, str(unknown), officialPhone="123").status_code == 404
    assert get_addresses(admin_api_client, str(unknown)).status_code == 404
    assert upsert_address(admin_api_client, str(unknown), addressType="Current").status_code == 404
