"""API smoke suite for Module 05 — Employee Management (Phase 3, first batch).

Proof-of-concept coverage, automated from qa/05-employee/cases/*.yaml. Deliberately narrow — see
qa/automation/README.md "Employee smoke suite (Phase 3)" for what is and isn't covered yet.

Requires QA_TENANT_A_HOST and QA_A_ADMIN_USERNAME/QA_A_ADMIN_PASSWORD for every test except the
authorization-denial one, which additionally needs QA_A_EMPLOYEE_USERNAME/QA_A_EMPLOYEE_PASSWORD
(a caller holding only the plain, no-Employee-permission `Employee` role) and skips on its own if
that pair isn't set — "where credentials exist", per the brief.

Every employee this suite creates is prefixed with `data.test_data.TEST_DATA_MARKER` and deleted
by the end of the test (via the `created_employee` fixture, or explicit cleanup for a test that
creates more than one); no pre-existing employee is ever read, edited or deleted.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_bank_detail,
    create_full,
    create_personal_details,
    delete_bank_detail,
    delete_employee,
    get_addresses,
    get_bank_details,
    get_contact,
    get_employment,
    get_portal_account,
    get_supervisor,
    list_banks,
    upsert_address,
    upsert_contact,
    upsert_employment,
    upsert_supervisor,
    update_bank_detail,
)
from data.test_data import (
    employee_user_a,
    today_iso,
    unique_employee_code,
    unique_employee_email,
    unique_last_name,
)
from utils.env_utils import require_env

pytestmark = [allure.feature("Employee Management"), pytest.mark.api]


@allure.story("Employee create")
@allure.title("EMP-CREATE-004 — a duplicate employee code (any case) is rejected with 409")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_duplicate_employee_code_is_rejected(admin_api_client):
    code = unique_employee_code()
    common_fields = dict(
        firstName="QaAuto",
        lastName=unique_last_name(),
        email=unique_employee_email(),
        dateOfJoining=today_iso(),
    )

    first = create_full(admin_api_client, employeeCode=code, **common_fields)
    assert first.status_code == 201, first.text
    employee_id = first.json()["data"]["id"]

    try:
        with allure.step("Retry with the same code in a different case"):
            duplicate = create_full(
                admin_api_client,
                employeeCode=code.swapcase(),
                firstName="QaAuto",
                lastName=unique_last_name(),
                email=unique_employee_email(),
                dateOfJoining=today_iso(),
            )
            assert duplicate.status_code == 409, duplicate.text
    finally:
        delete_employee(admin_api_client, employee_id)


@allure.story("Contact and address")
@allure.title("EMP-CA-001 — contact upsert and address create both save and read back correctly")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_contact_and_address_save(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    official_email = unique_employee_email()

    with allure.step("Upsert Contact and read it back"):
        contact_response = upsert_contact(
            admin_api_client, employee_id,
            officialEmail=official_email, officialPhone="+1 555 0100 200",
        )
        assert contact_response.status_code == 200, contact_response.text
        fetched_contact = get_contact(admin_api_client, employee_id)
        assert fetched_contact.status_code == 200, fetched_contact.text
        assert fetched_contact.json()["data"]["officialEmail"] == official_email

    with allure.step("Create a Current address and read it back"):
        address_response = upsert_address(
            admin_api_client, employee_id,
            addressType="Current", country="Testland", state="Test State",
            city="Test City", zipCode="123456", addressLine1="1 QA Automation Way",
        )
        assert address_response.status_code == 200, address_response.text
        fetched_addresses = get_addresses(admin_api_client, employee_id)
        assert fetched_addresses.status_code == 200, fetched_addresses.text
        addresses = fetched_addresses.json()["data"]
        assert any(a["city"] == "Test City" and a["addressType"] == "Current" for a in addresses)


@allure.story("Bank details")
@allure.title("EMP-BANK-001 / EMP-BANK-008 — invalid bank id is rejected; a valid record CRUDs and is masked on read")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_bank_detail_crud_lifecycle(admin_api_client, created_employee):
    employee_id = created_employee["id"]

    banks_response = list_banks(admin_api_client)
    assert banks_response.status_code == 200, banks_response.text
    active_banks = banks_response.json()["data"]
    if not active_banks:
        pytest.skip("No active Bank master data in this tenant — cannot exercise bank-details CRUD.")
    bank_id = active_banks[0]["id"]
    account_number = f"QAAUTO{uuid.uuid4().hex[:12]}"

    with allure.step("EMP-BANK-001: a nonexistent BankId is rejected on create"):
        invalid = create_bank_detail(
            admin_api_client, employee_id,
            bankId=str(uuid.uuid4()), accountHolderName="QA Automation", accountNumber=account_number,
            accountType="Savings", accountPurpose="Salary", status="Active",
        )
        assert invalid.status_code == 400, invalid.text

    with allure.step("Create with a valid, active BankId"):
        created = create_bank_detail(
            admin_api_client, employee_id,
            bankId=bank_id, accountHolderName="QA Automation", accountNumber=account_number,
            accountType="Savings", accountPurpose="Salary", status="Active",
        )
        assert created.status_code == 201, created.text
        bank_detail = created.json()["data"]
        bank_detail_id = bank_detail["id"]

    try:
        with allure.step("EMP-BANK-008: the list read returns a masked account number, not the raw value"):
            listed = get_bank_details(admin_api_client, employee_id)
            assert listed.status_code == 200, listed.text
            match = next(b for b in listed.json()["data"] if b["id"] == bank_detail_id)
            assert match["maskedAccountNumber"] != account_number
            assert match["maskedAccountNumber"], "Expected a non-empty masked account number"

        with allure.step("Update the branch name"):
            updated = update_bank_detail(
                admin_api_client, employee_id, bank_detail_id,
                bankId=bank_id, accountHolderName="QA Automation", accountNumber=account_number,
                accountType="Savings", accountPurpose="Salary", status="Active", branchName="QA Branch",
            )
            assert updated.status_code == 200, updated.text
            assert updated.json()["data"]["branchName"] == "QA Branch"
    finally:
        with allure.step("Delete the bank detail"):
            deleted = delete_bank_detail(admin_api_client, employee_id, bank_detail_id)
            assert deleted.status_code == 200, deleted.text


@allure.story("Employment")
@allure.title("EMP-JOIN-001 — the Employment (joining) record upserts and displays correctly")
@pytest.mark.smoke
@pytest.mark.regression
def test_employment_details_round_trip(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    first_hired = today_iso()

    upsert_response = upsert_employment(
        admin_api_client, employee_id,
        firstHiredDate=first_hired, dateOfJoining=first_hired, jobStatus="Confirmed",
    )
    assert upsert_response.status_code == 200, upsert_response.text

    get_response = get_employment(admin_api_client, employee_id)
    assert get_response.status_code == 200, get_response.text
    employment = get_response.json()["data"]
    assert employment["firstHiredDate"] == first_hired
    assert employment["dateOfJoining"] == first_hired
    assert employment["jobStatus"] == "Confirmed"


@allure.story("Reporting manager")
@allure.title("EMP-MGR-005 — an L2 manager (a free peer field) is assignable and displays code+name")
@pytest.mark.smoke
@pytest.mark.regression
def test_reporting_manager_display(admin_api_client, created_employee):
    manager_response = create_personal_details(
        admin_api_client, firstName="QaAuto", lastName=unique_last_name(prefix="QAAUTOMGR"), dateOfJoining=today_iso(),
    )
    assert manager_response.status_code == 201, manager_response.text
    manager = manager_response.json()["data"]

    try:
        subject_id = created_employee["id"]
        upsert_response = upsert_supervisor(admin_api_client, subject_id, l2ManagerId=manager["id"])
        assert upsert_response.status_code == 200, upsert_response.text

        get_response = get_supervisor(admin_api_client, subject_id)
        assert get_response.status_code == 200, get_response.text
        supervisor = get_response.json()["data"]
        assert supervisor["l2ManagerId"] == manager["id"]
        assert supervisor["l2ManagerName"], "Expected the manager's display name to be resolved, not just the id"
    finally:
        delete_employee(admin_api_client, manager["id"])


@allure.story("Portal account")
@allure.title("Portal account linking state is visible for a freshly created employee, where accessible")
@pytest.mark.smoke
@pytest.mark.regression
def test_portal_account_visibility(admin_api_client, created_employee):
    response = get_portal_account(admin_api_client, created_employee["id"])
    if response.status_code == 403:
        pytest.skip("The configured QA_A_ADMIN user does not hold User.View — portal-account visibility not accessible.")
    assert response.status_code == 200, response.text
    account = response.json()["data"]
    assert account["employeeId"] == created_employee["id"]
    assert account["state"], "Expected a non-empty linking state (e.g. NotLinked/Pending/Active)"


@allure.story("Authorization")
@allure.title("EMP-SEC-002 — unauthenticated is 401; authenticated-but-unauthorized is 403, distinctly")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_authorization_denial_for_unauthorized_user(api_client_factory, settings):
    from core.auth_helpers import api_login

    host = require_env("QA_TENANT_A_HOST")
    user = employee_user_a()

    with allure.step("No bearer token at all -> 401"):
        anonymous_client = api_client_factory(host)
        anonymous_response = anonymous_client.get("/api/employees")
        assert anonymous_response.status_code == 401, anonymous_response.text

    with allure.step("Signed in as a plain Employee-role user (no Employee.View) -> 403"):
        employee_client = api_client_factory(host)
        login_response = api_login(employee_client, user.identifier, user.password)
        assert login_response.status_code == 200, login_response.text
        token = login_response.json()["data"]["accessToken"]
        employee_client.with_bearer(token)

        denied_response = employee_client.get("/api/employees")
        assert denied_response.status_code == 403, denied_response.text
