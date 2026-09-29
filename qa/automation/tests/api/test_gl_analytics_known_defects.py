"""Known-defect regression checks for GL accounting (Module 16) and Payroll Analytics (Module 17).

Each test asserts the correct contract, so it FAILS while its defect is present and passes once the
defect is fixed. They are marked `known_defect`: they stay visible in every run, and
`-m "not known_defect"` deselects them explicitly when a green gate is needed.

- CR-275: no API cancels or reverses a payroll journal.
- CR-276: a journal never reaches `Exported`; export is a stateless GET.
- CR-278: journal history is written but no API returns it.
- CR-305: analytics CSV exports answer 400 for an unknown run, where the JSON endpoints answer 404.
- F1 (analytics): the variance, findings, exceptions and analytics-controls lists bind the abstract
  `PagedQuery` and return 500 (same root cause as the GL list F1 in test_payroll_known_defects.py).
- F6: component variance sums superseded (IsCurrent = false) result components.
- F7: component variance never matches a component to the comparison run, so BaseAmount is 0.

All checks are read-only. Route probes use a random id, so they can't change data even once the
defect is fixed. Sandbox checks read only runs and journals recorded in the QAAUTO manifest.
Defect write-ups: docs/qa/payroll-sandbox-setup.md §8; CR ids: qa/16-*/clarifications.yaml and
qa/17-*/clarifications.yaml.
"""

from __future__ import annotations

import base64
import json
import uuid
from decimal import Decimal

import allure
import pytest

from sandbox import payroll_sandbox as sb
from utils.allure_evidence import attach_json, known_defect

pytestmark = [
    allure.feature("Payroll"),
    allure.story("GL + Analytics known-defect regression checks"),
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


def _tag(qa_case: str, defect: str, title: str) -> None:
    allure.dynamic.title(f"{qa_case} ({defect}): {title}")
    allure.dynamic.tag(qa_case)
    allure.dynamic.label("qa_case", qa_case)


@pytest.fixture
def sandbox_cycles(admin_api_client) -> list[tuple[str, dict]]:
    """Complete sandbox months, oldest first, when the manifest is pinned to the signed-in tenant."""
    manifest = sb.load_manifest()
    if (manifest.get("tenant", {}).get("tid") or "").lower() != str(_claims(admin_api_client).get("tid")).lower():
        pytest.skip("The QAAUTO sandbox manifest is missing or pinned to another tenant; no sandbox evidence to read.")
    cycles = sorted((k, c) for k, c in manifest.get("cycles", {}).items() if c.get("state") == "complete" and c.get("payrollRunId"))
    if len(cycles) < 2:
        pytest.skip("Fewer than two complete sandbox months; run the payroll happy-path or GL/analytics E2E first.")
    return cycles


@pytest.fixture
def posted_cycle(sandbox_cycles) -> tuple[str, dict]:
    posted = [(k, c) for k, c in sandbox_cycles if "gl_journal_exported_posted" in c.get("steps", {})]
    if not posted:
        pytest.skip("No sandbox journal has been posted and exported yet; run test_gl_analytics_e2e.py first.")
    return posted[-1]


# --- Module 16: GL ---


@pytest.mark.parametrize("action", ["cancel", "reverse"])
@known_defect("CR-275")
def test_journal_cancel_or_reverse_endpoint_exists(admin_api_client, action):
    _tag("GLACC-REV-001", "CR-275", f"POST /api/payroll/accounting/{{id}}/{action} is a route")
    probe = str(uuid.uuid4())
    with allure.step(f"POST /api/payroll/accounting/<random id>/{action} (a real route answers with an API response)"):
        response = admin_api_client.post(f"/api/payroll/accounting/{probe}/{action}")
        attach_json("response", {"status": response.status_code, "contentType": response.headers.get("Content-Type"),
                                 "body": response.text[:200]})
    assert response.headers.get("Content-Type", "").startswith("application/json"), (
        f"KNOWN DEFECT CR-275: POST /api/payroll/accounting/{{id}}/{action} is not a route ({response.status_code}, empty body). "
        "No API cancels or reverses a journal, so a generated journal permanently blocks regeneration for its run.")


@known_defect("CR-276")
def test_exported_journal_reaches_exported_status(admin_api_client, posted_cycle):
    _tag("GLACC-REV-004", "CR-276", "a Posted journal that was exported reports status Exported")
    key, cycle = posted_cycle
    with allure.step(f"GET the sandbox journal of {key}, which the E2E exported while Posted"):
        response = admin_api_client.get(f"/api/payroll/accounting/{cycle['glJournalId']}")
        assert response.status_code == 200, response.text[:300]
        journal = response.json()["data"]
        attach_json("journal status", {"cycle": key, "journalNumber": journal["journalNumber"], "status": journal["status"],
                                       "exportedPostedAtUtc": cycle["steps"]["gl_journal_exported_posted"]["atUtc"]})
    assert journal["status"] == "Exported", (
        f"KNOWN DEFECT CR-276: sandbox journal {journal['journalNumber']} was exported while Posted, and its status is still "
        f"{journal['status']}. PayrollJournalStatus.Exported is never set by any code path.")


@known_defect("CR-278")
def test_journal_history_is_exposed(admin_api_client, posted_cycle):
    _tag("GLACC-HIST-003", "CR-278", "the journal's Generated/Validated/Approved/Posted history is readable through the API")
    key, cycle = posted_cycle
    path = f"/api/payroll/accounting/{cycle['glJournalId']}"
    with allure.step(f"GET {path} and GET {path}/history"):
        journal = admin_api_client.get(path).json()["data"]
        history_route = admin_api_client.get(f"{path}/history")
        history = journal.get("history")
        if history is None and history_route.headers.get("Content-Type", "").startswith("application/json"):
            history = history_route.json().get("data")
        attach_json("history evidence", {"cycle": key, "dtoKeys": sorted(journal), "historyRouteStatus": history_route.status_code,
                                         "history": history})
    assert history, (
        "KNOWN DEFECT CR-278: PayrollJournalDto has no history field and there is no /history route, although every "
        "transition writes a PayrollJournalHistories row. Payroll.Accounting.ViewHistory gates nothing.")
    assert {"Generated", "Validated", "Approved", "Posted"} <= {h.get("changeType") for h in history}, history


# --- Module 17: Analytics ---


@pytest.mark.parametrize("export", ["variance.csv", "findings.csv", "summary.csv", "control-totals.csv"])
@known_defect("CR-305")
def test_analytics_csv_unknown_run_is_not_found(admin_api_client, export):
    _tag("PYA-CSV-007", "CR-305", f"GET runs/<unknown>/{export} answers 404 like its JSON counterpart")
    probe = str(uuid.uuid4())
    with allure.step("Control: the JSON overview for the same unknown run is 404"):
        control = admin_api_client.get(f"/api/payroll/analytics/runs/{probe}/overview")
        assert control.status_code == 404, control.text[:300]
    with allure.step(f"GET /api/payroll/analytics/runs/<unknown>/{export}"):
        response = admin_api_client.get(f"/api/payroll/analytics/runs/{probe}/{export}")
        attach_json("response", {"status": response.status_code, "contentType": response.headers.get("Content-Type"),
                                 "body": response.text[:200]})
    assert response.status_code == 404, (
        f"KNOWN DEFECT CR-305: {export} for an unknown run returned {response.status_code} ({response.text[:80]!r}); "
        "the JSON endpoints return 404. Every analytics CSV action maps any failure to 400.")


@pytest.mark.parametrize(
    ("qa_case", "path", "params"),
    [
        ("PYA-VAR-008", "variance", {"search": "QaAutoPayroll", "page": 1, "pageSize": 10}),
        ("PYA-FIND-002", "findings", {"search": "BankAdvice", "page": 1, "pageSize": 10}),
        ("PYA-EXC-001", "/api/payroll/exceptions", {"page": 1, "pageSize": 10}),
        ("PYA-EXC-007", "/api/payroll/exceptions/export.csv", {}),
        ("PYA-CTRL-001", "/api/payroll/analytics-controls", {"page": 1, "pageSize": 10}),
    ],
    ids=["variance", "findings", "exceptions", "exceptions-csv", "analytics-controls"],
)
@known_defect("F1")
def test_analytics_list_endpoint_answers(admin_api_client, sandbox_cycles, qa_case, path, params):
    key, cycle = sandbox_cycles[-1]
    url = path if path.startswith("/") else f"/api/payroll/analytics/runs/{cycle['payrollRunId']}/{path}"
    if path == "variance":
        params = {**params, "compareRunId": sandbox_cycles[-2][1]["payrollRunId"]}
    _tag(qa_case, "F1", f"GET {url.replace(cycle['payrollRunId'], '{sandbox run}')} answers 200, not 500")
    with allure.step(f"GET {url} {params}"):
        response = admin_api_client.get(url, params=params or None)
        attach_json("response (status + message)", {"status": response.status_code, "message": _message(response)})
    assert response.status_code == 200, f"KNOWN DEFECT F1: GET {url} returned {response.status_code}: {_message(response)}"
    if url.endswith(".csv"):
        assert response.headers.get("Content-Type", "").startswith("text/csv")
    else:
        data = response.json()["data"]
        assert isinstance(data.get("items"), list) and isinstance(data.get("totalCount"), int), data


def _component_rows(client, run_id: str, compare_run_id: str) -> list:
    response = client.get(f"/api/payroll/analytics/runs/{run_id}/components", params={"compareRunId": compare_run_id})
    assert response.status_code == 200, response.text[:300]
    return [r for r in response.json()["data"] if r["componentCode"] == sb.COMPONENT_CODE]


def _current_component_total(client, run_id: str) -> Decimal:
    response = client.get(f"/api/payroll/runs/{run_id}/results", params={"pageSize": 100})
    assert response.status_code == 200, response.text[:300]
    return sum((Decimal(str(c["calculatedAmount"])) for r in response.json()["data"]["items"] for c in r["components"]
                if c["componentCode"] == sb.COMPONENT_CODE), Decimal(0))


@known_defect("F6")
def test_component_variance_counts_current_results_only(admin_api_client, sandbox_cycles):
    _tag("PYA-CVAR-001", "F6", "component variance CurrentAmount equals the run's current results (superseded versions excluded)")
    (prev_key, prev), (key, cycle) = sandbox_cycles[-2], sandbox_cycles[-1]
    with allure.step(f"Sandbox {key} vs {prev_key}: current results' {sb.COMPONENT_CODE} total, and the component variance row"):
        expected = _current_component_total(admin_api_client, cycle["payrollRunId"])
        rows = _component_rows(admin_api_client, cycle["payrollRunId"], prev["payrollRunId"])
        attach_json("component variance (sandbox runs)", {"cycle": key, "compare": prev_key, "currentResultsTotal": str(expected), "rows": rows})
    assert len(rows) == 1, rows
    assert Decimal(str(rows[0]["currentAmount"])) == expected, (
        f"KNOWN DEFECT F6: CurrentAmount is {rows[0]['currentAmount']}, the current results total {expected}. "
        "GetComponentVarianceAsync sums PayrollResultComponents without the IsCurrent/Calculated filter the other analytics "
        "reads use, so each recalculation adds the superseded version again.")


@known_defect("F7")
def test_component_variance_matches_comparison_run(admin_api_client, sandbox_cycles):
    _tag("PYA-CVAR-001", "F7", "a component present in both runs gets its BaseAmount from the comparison run")
    (prev_key, prev), (key, cycle) = sandbox_cycles[-2], sandbox_cycles[-1]
    with allure.step(f"Sandbox {key} vs {prev_key}: both runs pay the same {sb.COMPONENT_CODE} amount"):
        rows = _component_rows(admin_api_client, cycle["payrollRunId"], prev["payrollRunId"])
        reverse = _component_rows(admin_api_client, prev["payrollRunId"], cycle["payrollRunId"])
        attach_json("component variance both ways (sandbox runs)", {"cycle": key, "compare": prev_key, "rows": rows, "reverseRows": reverse})
    assert len(rows) == 1, rows
    row = rows[0]
    assert Decimal(str(row["baseAmount"])) == Decimal(str(row["currentAmount"])) and Decimal(str(row["delta"])) == 0, (
        f"KNOWN DEFECT F7: two sandbox runs with identical pay report BaseAmount {row['baseAmount']}, CurrentAmount "
        f"{row['currentAmount']}, Delta {row['delta']}. GetComponentVarianceAsync compares anonymous-type keys with ==, which is "
        "reference equality in C#, so a comparison-run amount is never found.")
