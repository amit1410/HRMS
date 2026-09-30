"""API regression suite for Module 05 — Employee Bank Details.

Automated from qa/05-employee/cases/05-bank-details.yaml. Covers the bank-master reference check,
field validation, one-active-record-per-purpose, the create-must-be-Active rule, the one-way
Frozen/Closed transition into immutable history, soft-delete idempotency, purpose reuse, masking, and
tenant/auth isolation. Bank writes require EmployeeSensitive.Edit (the QA admin holds it).

Every bank detail is created against a disposable employee that is deleted afterward, so nothing
touches real data. Steps needing an *inactive* bank master (there is only one active QAAUTO bank in
this environment) are exercised through the nonexistent-bank path instead.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_bank_detail,
    delete_bank_detail,
    get_bank_detail_for_edit,
    get_bank_details,
    list_banks,
    update_bank_detail,
)
from data.test_data import random_suffix
from utils.allure_evidence import qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


@pytest.fixture
def bank_id(admin_api_client):
    response = list_banks(admin_api_client)
    assert response.status_code == 200, response.text
    active = response.json()["data"]
    if not active:
        pytest.skip("No active Bank master data in this tenant — cannot exercise bank-details CRUD.")
    return active[0]["id"]


def _acct():
    return f"QAAUTO{random_suffix(12)}"


def _new(admin_api_client, employee_id, bank_id, **overrides):
    body = dict(
        bankId=bank_id, accountHolderName="QA Automation", accountNumber=_acct(),
        accountType="Savings", accountPurpose="Salary", status="Active",
    )
    body.update(overrides)
    return create_bank_detail(admin_api_client, employee_id, **body)


@allure.story("Bank details")
@allure.title("EMP-BANK-001 — a nonexistent bank id is rejected; a valid active bank is accepted")
@qa_cases("EMP-BANK-001")
def test_bank_reference_check(admin_api_client, created_employee, bank_id):
    employee_id = created_employee["id"]
    with allure.step("nonexistent bank id => 400"):
        assert _new(admin_api_client, employee_id, str(uuid.uuid4())).status_code == 400
    with allure.step("valid active bank => 201"):
        assert _new(admin_api_client, employee_id, bank_id).status_code == 201


@allure.story("Bank details")
@allure.title("EMP-BANK-002 — required/optional field validation matrix")
@qa_cases("EMP-BANK-002")
def test_bank_field_validation(admin_api_client, created_employee, bank_id):
    employee_id = created_employee["id"]
    with allure.step("missing account holder name => 400"):
        assert _new(admin_api_client, employee_id, bank_id, accountHolderName="").status_code == 400
    with allure.step("31-character account number => 400"):
        assert _new(admin_api_client, employee_id, bank_id, accountNumber="9" * 31).status_code == 400
    with allure.step("IFSC with a hyphen => 400"):
        assert _new(admin_api_client, employee_id, bank_id, ifscCode="SBI-0001").status_code == 400
    with allure.step("valid alphanumeric IFSC => 201"):
        assert _new(admin_api_client, employee_id, bank_id, ifscCode="SBIN0001234").status_code == 201


@allure.story("Bank details")
@allure.title("EMP-BANK-003 / EMP-BANK-004 — one active record per purpose; different purposes coexist")
@qa_cases("EMP-BANK-003", "EMP-BANK-004")
def test_one_active_per_purpose(admin_api_client, created_employee, bank_id):
    employee_id = created_employee["id"]
    assert _new(admin_api_client, employee_id, bank_id, accountPurpose="Salary").status_code == 201
    with allure.step("second active Salary record => 409"):
        assert _new(admin_api_client, employee_id, bank_id, accountPurpose="Salary").status_code == 409
    with allure.step("a Gratuity record coexists => 201"):
        assert _new(admin_api_client, employee_id, bank_id, accountPurpose="Gratuity").status_code == 201
    with allure.step("both rows are listed and masked"):
        rows = get_bank_details(admin_api_client, employee_id).json()["data"]
        assert len(rows) == 2
        assert all(r.get("maskedAccountNumber") for r in rows)


@allure.story("Bank details")
@allure.title("EMP-BANK-005 — a new bank detail must be created as Active")
@qa_cases("EMP-BANK-005")
def test_create_must_be_active(admin_api_client, created_employee, bank_id):
    employee_id = created_employee["id"]
    assert _new(admin_api_client, employee_id, bank_id, status="Frozen").status_code == 400
    assert _new(admin_api_client, employee_id, bank_id, status="Closed").status_code == 400
    assert _new(admin_api_client, employee_id, bank_id, status="Active").status_code == 201


@allure.story("Bank details")
@allure.title("EMP-BANK-006 — Frozen/Closed is a one-way transition into immutable history")
@qa_cases("EMP-BANK-006")
def test_frozen_is_immutable(admin_api_client, created_employee, bank_id):
    employee_id = created_employee["id"]
    created = _new(admin_api_client, employee_id, bank_id)
    assert created.status_code == 201, created.text
    detail = created.json()["data"]
    account_number = None  # not needed; update sends full body
    with allure.step("move Active -> Frozen => 200"):
        frozen = update_bank_detail(
            admin_api_client, employee_id, detail["id"],
            bankId=bank_id, accountHolderName="QA Automation", accountNumber=_acct(),
            accountType="Savings", accountPurpose="Salary", status="Frozen",
        )
        assert frozen.status_code == 200, frozen.text
    with allure.step("edit any field on the Frozen record => 409"):
        edit = update_bank_detail(
            admin_api_client, employee_id, detail["id"],
            bankId=bank_id, accountHolderName="Changed Name", accountNumber=_acct(),
            accountType="Savings", accountPurpose="Salary", status="Frozen", branchName="X",
        )
        assert edit.status_code == 409, edit.text
    with allure.step("reactivate the Frozen record => 409"):
        react = update_bank_detail(
            admin_api_client, employee_id, detail["id"],
            bankId=bank_id, accountHolderName="QA Automation", accountNumber=_acct(),
            accountType="Savings", accountPurpose="Salary", status="Active",
        )
        assert react.status_code == 409, react.text


@allure.story("Bank details")
@allure.title("EMP-BANK-007 / EMP-BANK-014 — delete soft-deactivates (idempotent) and frees the purpose for reuse")
@qa_cases("EMP-BANK-007", "EMP-BANK-014")
def test_delete_is_soft_and_purpose_reusable(admin_api_client, created_employee, bank_id):
    employee_id = created_employee["id"]
    created = _new(admin_api_client, employee_id, bank_id, accountPurpose="Salary")
    assert created.status_code == 201, created.text
    detail_id = created.json()["data"]["id"]
    with allure.step("delete the active Salary record => 200"):
        assert delete_bank_detail(admin_api_client, employee_id, detail_id).status_code == 200
    with allure.step("delete the same (now historical) record again => 200 (idempotent)"):
        assert delete_bank_detail(admin_api_client, employee_id, detail_id).status_code == 200
    with allure.step("the Salary purpose is free again => 201"):
        assert _new(admin_api_client, employee_id, bank_id, accountPurpose="Salary").status_code == 201


@allure.story("Bank details")
@allure.title("EMP-BANK-008 — account number is masked on the list; raw only via the sensitive-details endpoint")
@qa_cases("EMP-BANK-008")
def test_masked_list_and_raw_sensitive(admin_api_client, created_employee, bank_id):
    employee_id = created_employee["id"]
    account_number = _acct()
    created = _new(admin_api_client, employee_id, bank_id, accountNumber=account_number)
    assert created.status_code == 201, created.text
    detail_id = created.json()["data"]["id"]
    with allure.step("list read is masked"):
        row = next(r for r in get_bank_details(admin_api_client, employee_id).json()["data"] if r["id"] == detail_id)
        assert row["maskedAccountNumber"] and row["maskedAccountNumber"] != account_number
    with allure.step("sensitive-details returns the raw value (caller holds EmployeeSensitive.View)"):
        raw = get_bank_detail_for_edit(admin_api_client, employee_id, detail_id)
        assert raw.status_code == 200, raw.text
        assert raw.json()["data"]["accountNumber"] == account_number


@allure.story("Bank details")
@allure.title("EMP-BANK-012 — bank-detail endpoints reject an unauthenticated caller with 401")
@qa_cases("EMP-BANK-012")
def test_bank_requires_authentication(api_client_factory, created_employee):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert anon.get(f"/api/employees/{created_employee['id']}/bank-details").status_code == 401


@allure.story("Bank details")
@allure.title("EMP-BANK-013 — bank details on an unknown/cross-tenant employee id is a 404")
@qa_cases("EMP-BANK-013")
def test_bank_unknown_employee_is_404(admin_api_client, bank_id):
    unknown = uuid.uuid4()
    assert get_bank_details(admin_api_client, str(unknown)).status_code == 404
    assert _new(admin_api_client, str(unknown), bank_id).status_code == 404
