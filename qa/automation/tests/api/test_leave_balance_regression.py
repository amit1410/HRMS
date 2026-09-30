"""API regression suite for Module 07 — Leave balances (self-service) and Balance Import.

Automated from qa/07-leave/cases/05-balances-import.yaml (LEAVE-BAL-*, LEAVE-IMPORT-*).

Self-service balance reads are non-destructive. On the Import side this suite deliberately performs
NO real upload: a committed opening balance is permanent tenant state with no delete path, which
would violate safe-cleanup. It instead pins the read/template/authorization contract and the
wrong-content-type guard (a JSON body to the multipart validate endpoint is rejected before any
batch is created). Per CR-78, only SuperAdmin/TenantAdmin hold BalanceImport in the seeded matrix.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.leave_api import (
    get_import_batch,
    get_import_history,
    get_import_template,
    get_my_leave_balances,
    validate_import_json,
)
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]


# --- Self-service balance --------------------------------------------------------------------


@allure.story("Leave balance")
@allure.title("LEAVE-BAL-001 — /mine returns a well-formed per-LeaveType balance list for the caller")
@qa_cases("LEAVE-BAL-001")
@pytest.mark.critical
def test_my_balance_shape(employee_api_client):
    response = get_my_leave_balances(employee_api_client)
    assert response.status_code == 200, response.text
    assert isinstance(response.json()["data"], list)


@allure.story("Leave balance")
@allure.title("LEAVE-BAL-002 — where a balance row exists, Available == Granted − Reserved − Consumed")
@qa_cases("LEAVE-BAL-002")
def test_available_is_computed_live(employee_api_client):
    rows = get_my_leave_balances(employee_api_client).json()["data"]
    allocated = [r for r in rows if all(k in r for k in ("granted", "reserved", "consumed", "available"))]
    if not allocated:
        pytest.skip("The QA employee has no Allocated balance row to check the Available invariant against.")
    for r in allocated:
        assert abs(r["available"] - (r["granted"] - r["reserved"] - r["consumed"])) < 1e-6, r


@allure.story("Leave balance")
@allure.title("LEAVE-BAL-008 / CR-80 — /mine is strictly self-scoped: an employeeId param cannot reach another employee")
@qa_cases("LEAVE-BAL-008")
@cr_refs("CR-80")
def test_balance_is_self_scoped_only(employee_api_client):
    own = get_my_leave_balances(employee_api_client)
    assert own.status_code == 200, own.text
    # There is no HR-wide "view any balance" endpoint; a stray employeeId must be ignored, not honored.
    probed = employee_api_client.get("/api/leave-balances/mine", params={"employeeId": str(uuid.uuid4())})
    assert probed.status_code == 200, probed.text
    assert probed.json()["data"] == own.json()["data"]


@allure.story("Leave balance")
@allure.title("LEAVE-AUTHZ-002 — an anonymous balance call is rejected 401")
@qa_cases("LEAVE-AUTHZ-002")
def test_balance_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert get_my_leave_balances(anon).status_code == 401


# --- Balance Import ------------------------------------------------------------------------------


@allure.story("Leave balance import")
@allure.title("LEAVE-IMPORT-001 — the fixed CSV template is downloadable with the documented header")
@qa_cases("LEAVE-IMPORT-001")
def test_import_template_download(admin_api_client):
    response = get_import_template(admin_api_client)
    assert response.status_code == 200, response.text
    header = response.text.lstrip("﻿").splitlines()[0]
    for column in ("EmployeeCode", "LeaveTypeCode", "LeavePeriod", "OpeningBalance", "EffectiveDate"):
        assert column in header, f"template header missing {column!r}: {header!r}"


@allure.story("Leave balance import")
@allure.title("LEAVE-IMPORT-016 — import history is readable for an authorized admin")
@qa_cases("LEAVE-IMPORT-016")
def test_import_history_readable(admin_api_client):
    response = get_import_history(admin_api_client)
    assert response.status_code == 200, response.text
    assert isinstance(response.json()["data"], list)


@allure.story("Leave balance import")
@allure.title("LEAVE-IMPORT-016 — an unknown import batch id is a clean 404")
@qa_cases("LEAVE-IMPORT-016")
def test_unknown_import_batch_is_404(admin_api_client):
    assert get_import_batch(admin_api_client, str(uuid.uuid4())).status_code == 404


@allure.story("Leave balance import")
@allure.title("LEAVE-IMPORT-003 — validate rejects a non-multipart (JSON) body before creating any batch")
@qa_cases("LEAVE-IMPORT-003")
def test_import_validate_rejects_wrong_content_type(admin_api_client):
    before = len(get_import_history(admin_api_client).json()["data"])
    response = validate_import_json(admin_api_client, {})
    assert response.status_code in (400, 415), response.text
    after = len(get_import_history(admin_api_client).json()["data"])
    assert after == before, "a rejected validate must not create an import batch"


@allure.story("Leave balance import")
@allure.title("LEAVE-IMPORT-018 / CR-78 — import reads need BalanceImport/ViewImportHistory: Employee and Manager get 403")
@qa_cases("LEAVE-IMPORT-018")
@cr_refs("CR-78")
@pytest.mark.critical
def test_import_requires_permission(employee_api_client, manager_api_client):
    # NOTE: the multipart `validate` endpoint answers 415 (wrong content type) BEFORE its authorization
    # filter runs, so a JSON body cannot demonstrate its 403 — the template (BalanceImport) and history
    # (BalanceViewImportHistory) GETs are the clean permission probes.
    for client in (employee_api_client, manager_api_client):
        assert get_import_template(client).status_code == 403
        assert get_import_history(client).status_code == 403
