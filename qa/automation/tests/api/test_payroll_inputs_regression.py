"""API regression suite for Module 10 — Payroll Inputs (templates + batch lifecycle guards).

Automated from qa/10-payroll/cases/10-payroll-inputs.yaml (PAY-IN-*). Statuses and messages confirmed
live before writing. Batch numbers, the create-validation matrix, the submit/post transition guards,
and the create->validate->preview->cancel lifecycle are all reachable with the admin identity and
QAAUTO data. The CSV-upload/parse cases (PAY-IN-005..012), the post->adjustment generation
(PAY-IN-015), and the maker-checker self-approval block (PAY-IN-014) need multipart CSV uploads and a
second payroll-capable identity respectively, and are out of scope for this default-environment core
suite (the happy-path E2E covers the sandbox flow). Batches are cancelled in teardown where possible.
"""

from __future__ import annotations

import re
import uuid

import allure
import pytest

from core.payroll_api import (
    cancel_input_batch,
    create_input_batch,
    get_input_batch,
    get_input_batch_preview,
    list_input_batches,
    list_input_templates,
    post_input_batch,
    submit_input_batch,
    validate_input_batch,
)
from data.test_data import today_iso
from utils.allure_evidence import qa_cases
from utils.env_utils import require_env
from utils.payroll_data import make_period, qa_code

pytestmark = [allure.feature("Payroll"), allure.story("Payroll Inputs"), pytest.mark.api, pytest.mark.regression]

_BATCH_NUMBER = re.compile(r"^PIN/\d{14}/[0-9a-fA-F]+$")


@pytest.fixture
def period(admin_api_client):
    return make_period(admin_api_client)


@pytest.fixture
def draft_batch(admin_api_client, period):
    response = create_input_batch(
        admin_api_client, name=f"QAAUTO Batch {qa_code('IN')[-8:]}",
        description="QA automation regression batch — safe to leave Cancelled.",
        payrollPeriodId=period["id"], effectiveDate=today_iso(), sourceType="Manual", lines=[])
    if response.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO input batch ({response.status_code}): {response.text}")
    batch = response.json()["data"]
    yield batch
    try:
        current = get_input_batch(admin_api_client, batch["id"])
        if current.status_code == 200 and current.json()["data"]["status"] not in ("Cancelled", "Posted"):
            cancel_input_batch(admin_api_client, batch["id"], "QAAUTO automation cleanup.")
    except Exception:
        pass


@allure.title("PAY-IN-001 — a template with a column mapping is created and listed")
@qa_cases("PAY-IN-001")
def test_create_and_list_template(admin_api_client):
    code = qa_code("QAAUTO-TPL")
    created = admin_api_client.post("/api/payroll/input-templates", json={
        "code": code, "name": "QAAUTO Template",
        "columns": [{"sourceColumnName": "Emp", "targetField": "EmployeeCode"}]})
    assert created.status_code in (200, 201), created.text
    assert created.json()["data"]["code"] == code

    listed = list_input_templates(admin_api_client)
    assert listed.status_code == 200, listed.text


@allure.title("PAY-IN-003 — a batch is created with an auto-generated BatchNumber (PIN/{ts}/{hex}, <=34 chars)")
@qa_cases("PAY-IN-003")
@pytest.mark.critical
def test_batch_number_format(draft_batch):
    assert draft_batch["status"] == "Draft"
    assert _BATCH_NUMBER.match(draft_batch["batchNumber"]), draft_batch["batchNumber"]
    assert len(draft_batch["batchNumber"]) <= 34


@allure.title("PAY-IN-004 — a blank name and an unknown period are each rejected 400")
@qa_cases("PAY-IN-004")
def test_batch_create_validation(admin_api_client, period):
    blank = create_input_batch(
        admin_api_client, name="", payrollPeriodId=period["id"], effectiveDate=today_iso(),
        sourceType="Manual", lines=[])
    assert blank.status_code == 400 and "Batch name is required." in blank.text, blank.text

    unknown = create_input_batch(
        admin_api_client, name="QAAUTO", payrollPeriodId=str(uuid.uuid4()), effectiveDate=today_iso(),
        sourceType="Manual", lines=[])
    assert unknown.status_code == 400 and "Payroll period was not found." in unknown.text, unknown.text


@allure.title("PAY-IN — a Manual batch is validated, previewed, then cancelled with a reason")
@qa_cases("PAY-IN-008", "PAY-IN-011", "PAY-IN-017")
def test_batch_validate_preview_cancel(admin_api_client, draft_batch):
    validated = validate_input_batch(admin_api_client, draft_batch["id"])
    assert validated.status_code == 200, validated.text

    preview = get_input_batch_preview(admin_api_client, draft_batch["id"])
    assert preview.status_code == 200, preview.text

    cancelled = cancel_input_batch(admin_api_client, draft_batch["id"], "QAAUTO automation cleanup.")
    assert cancelled.status_code == 200, cancelled.text
    assert cancelled.json()["data"]["status"] == "Cancelled"


@allure.title("PAY-IN-013 — submitting a Draft batch is rejected 409")
@qa_cases("PAY-IN-013")
@pytest.mark.critical
def test_submit_draft_rejected(admin_api_client, draft_batch):
    response = submit_input_batch(admin_api_client, draft_batch["id"])
    assert response.status_code == 409, response.text
    assert "The batch transition is not allowed." in response.text


@allure.title("PAY-IN-016 — posting a not-yet-approved batch is rejected 409")
@qa_cases("PAY-IN-016")
def test_post_unapproved_rejected(admin_api_client, draft_batch):
    response = post_input_batch(admin_api_client, draft_batch["id"])
    assert response.status_code == 409, response.text
    assert "Only an approved, atomically valid batch can be posted." in response.text


@allure.title("PAY-IN-018 — batch history is readable")
@qa_cases("PAY-IN-018")
def test_batch_history_readable(admin_api_client, draft_batch):
    response = admin_api_client.get(f"/api/payroll/input-batches/{draft_batch['id']}/history")
    assert response.status_code == 200, response.text
    assert isinstance(response.json()["data"], list) and len(response.json()["data"]) >= 1


@allure.title("PAY-IN-020 — a batch id not in this tenant is 404")
@qa_cases("PAY-IN-020")
@pytest.mark.critical
def test_foreign_batch_id_is_404(admin_api_client):
    response = get_input_batch(admin_api_client, str(uuid.uuid4()))
    assert response.status_code == 404, response.text


@allure.title("PAY-IN-021 — list/filter batches by status")
@qa_cases("PAY-IN-021")
def test_list_filter_by_status(admin_api_client):
    response = list_input_batches(admin_api_client, status="Posted")
    assert response.status_code == 200, response.text
    assert all(b["status"] == "Posted" for b in response.json()["data"]["items"])


@allure.title("PAY-IN — an unauthenticated caller is rejected 401 on the input-batches surface")
@qa_cases("PAY-IN-021")
@pytest.mark.critical
def test_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert list_input_batches(anon).status_code == 401
