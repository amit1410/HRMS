"""Regression checks for the payroll defects reported in docs/qa/payroll-sandbox-setup.md §8.

Each test asserts the correct contract, so it FAILS while its defect is present and passes once the
defect is fixed. They are marked `known_defect`: they stay visible in every run, and
`-m "not known_defect"` deselects them explicitly when a green gate is needed.

- F1: the GL list endpoints bind the abstract `PagedQuery` from the query string and return 500.
- F2: `/swagger/v1/swagger.json` returns 500 (MasterImportController.Validate breaks generation).
- F3: `GET /api/payroll/controls` reports maker-checker and self-approval prevention as on when no
  control row exists, while `PayrollApprovalGuard` treats a missing row as "no controls". The
  QAAUTO sandbox's own Approved run, prepared and approved by the same user, is the live evidence.

All checks are read-only. F3 reads only runs recorded in the QAAUTO sandbox manifest.
"""

from __future__ import annotations

import base64
import json

import allure
import pytest

from sandbox import payroll_sandbox as sb
from utils.allure_evidence import attach_json, known_defect, qa_cases
from utils.env_utils import require_env

pytestmark = [
    allure.feature("Payroll"),
    allure.story("Known-defect regression checks"),
    pytest.mark.api,
    pytest.mark.regression,
    pytest.mark.known_defect,
]


def _message(response) -> str:
    try:
        return str(response.json().get("message"))[:300]
    except ValueError:
        return response.text[:300]


def _claims(client) -> dict:
    token = client._default_headers["Authorization"].split(" ", 1)[1]
    payload = token.split(".")[1]
    payload += "=" * (-len(payload) % 4)
    return json.loads(base64.urlsafe_b64decode(payload))


@pytest.mark.parametrize(
    ("path", "qa_case"),
    [
        ("/api/payroll/accounting/accounts", "GLACC-ACCT-007"),
        ("/api/payroll/accounting/configurations", "GLACC-CFG-001"),
        ("/api/payroll/accounting", "GLACC-RPT-003"),
    ],
    ids=["gl-accounts", "gl-configurations", "gl-journals"],
)
@known_defect("F1")
def test_gl_list_endpoint_returns_paged_result(admin_api_client, path, qa_case):
    allure.dynamic.title(f"{qa_case} (F1): GET {path} returns a paged list, not 500")
    allure.dynamic.tag(qa_case)
    allure.dynamic.label("qa_case", qa_case)

    with allure.step(f"GET {path}?page=1&pageSize=10"):
        response = admin_api_client.get(path, params={"page": 1, "pageSize": 10})
        attach_json("response (status + message)", {"status": response.status_code, "message": _message(response)})

    assert response.status_code == 200, f"KNOWN DEFECT F1: GET {path} returned {response.status_code}: {_message(response)}"
    data = response.json()["data"]
    assert isinstance(data.get("items"), list) and isinstance(data.get("totalCount"), int), data


@allure.title("AUTH-SEC-002 (F2): the Development OpenAPI document /swagger/v1/swagger.json generates")
@qa_cases("AUTH-SEC-002")
@known_defect("F2")
def test_swagger_document_generates(api_client_factory):
    client = api_client_factory(require_env("QA_TENANT_A_HOST"))

    with allure.step("Swagger UI is mapped (Development server)"):
        ui = client.get("/swagger/index.html")
        if ui.status_code != 200:
            pytest.skip(f"/swagger/index.html returned {ui.status_code}: not a Development server, so no OpenAPI document is expected.")

    with allure.step("GET /swagger/v1/swagger.json"):
        response = client.get("/swagger/v1/swagger.json", timeout=60)
        attach_json("response (status + message)", {"status": response.status_code, "message": _message(response)})

    assert response.status_code == 200, f"KNOWN DEFECT F2: swagger.json returned {response.status_code}: {_message(response)}"
    document = response.json()
    assert document.get("openapi") and document.get("paths"), "swagger.json is not an OpenAPI document."


@allure.title("PAY-PR-018 / F-PAY-082 (F3): GET /api/payroll/controls reports the maker-checker controls that approval enforces")
@qa_cases("PAY-PR-018", "F-PAY-082")
@known_defect("F3")
def test_payroll_controls_report_matches_approval_enforcement(admin_api_client):
    claims = _claims(admin_api_client)
    manifest = sb.load_manifest()
    if (manifest.get("tenant", {}).get("tid") or "").lower() != str(claims.get("tid")).lower():
        pytest.skip("The QAAUTO sandbox manifest is missing or pinned to another tenant; no sandbox approval evidence to read.")

    with allure.step("Find a sandbox run that was approved by the user who prepared it"):
        evidence = None
        for key, cycle in sorted(manifest.get("cycles", {}).items(), reverse=True):
            if "approved" not in cycle.get("steps", {}) or not cycle.get("payrollRunId"):
                continue
            run_id = cycle["payrollRunId"]
            run = admin_api_client.get(f"/api/payroll/runs/{run_id}")
            history = admin_api_client.get(f"/api/payroll/runs/{run_id}/history")
            assert run.status_code == 200 and history.status_code == 200, (run.status_code, history.status_code)
            run_data = run.json()["data"]
            approvers = [h["actorUserId"] for h in history.json()["data"] if h["changeType"] == "Approved"]
            if run_data["status"] in ("Approved", "Finalized") and approvers and approvers[0] == run_data["startedByUserId"]:
                evidence = {"cycle": key, "runId": run_id, "runNumber": run_data["runNumber"], "status": run_data["status"],
                            "startedByUserId": run_data["startedByUserId"], "approvedByUserId": approvers[0]}
                break
        if evidence is None:
            pytest.skip("No self-approved sandbox run in the manifest; run the payroll happy-path E2E first.")
        attach_json("self-approval evidence (sandbox run)", evidence)

    with allure.step("GET /api/payroll/controls"):
        response = admin_api_client.get("/api/payroll/controls")
        assert response.status_code == 200, response.text[:300]
        controls = response.json()["data"]
        attach_json("payroll-controls", controls)

    persisted = controls["id"] != sb.EMPTY_GUID
    reported = controls["requireMakerChecker"] and controls["preventSelfApproval"]
    with allure.step(f"Reported self-approval prevention ({reported}) matches enforcement (row persisted: {persisted})"):
        if persisted:
            # With a row, the endpoint and PayrollApprovalGuard read the same values; the historic
            # self-approval may predate the row, so it isn't evidence either way.
            return
        assert not reported, (
            "KNOWN DEFECT F3: no payroll control row exists (id is all zeros), and GET /api/payroll/controls reports "
            f"requireMakerChecker={controls['requireMakerChecker']} and preventSelfApproval={controls['preventSelfApproval']}. "
            f"PayrollApprovalGuard doesn't enforce them: sandbox run {evidence['runNumber']} was approved by the same user "
            "who prepared it. The endpoint reports a control that is not enforced.")
