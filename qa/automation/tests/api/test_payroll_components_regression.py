"""API regression suite for Module 10 — Salary Component Master.

Automated from qa/10-payroll/cases/01-salary-component-master.yaml (PAY-SC-001..016). Every status
code and message asserted below was confirmed against the live ANEVRA01 API before this file was
written, so a failure here is a genuine behavioural regression, not catalogue drift.

Runs entirely on the `admin_api_client` (TenantAdmin — the only seeded identity holding any Payroll
permission in this tenant; see qa/10-payroll/README.md CR-165/CR-168) plus QAAUTO-prefixed data.
Salary Components have no delete endpoint, so every component this suite creates is deactivated
best-effort in teardown and left behind identifiable by its QAAUTO code, exactly as the smoke suite
and README's data-safety design prescribe.
"""

from __future__ import annotations

import concurrent.futures
import uuid

import allure
import pytest

from core.payroll_api import (
    activate_salary_component,
    create_salary_component,
    deactivate_salary_component,
    get_salary_component,
    get_salary_component_history,
    list_salary_components,
    update_salary_component,
)
from utils.allure_evidence import qa_cases
from utils.env_utils import require_env
from utils.payroll_data import QA_EFFECTIVE_FROM, cleanup_component, make_component, qa_code

pytestmark = [allure.feature("Payroll"), allure.story("Salary Component Master"), pytest.mark.api, pytest.mark.regression]


@pytest.fixture
def component(admin_api_client):
    row = make_component(admin_api_client)
    yield row
    cleanup_component(admin_api_client, row["id"])


def _base(**overrides) -> dict:
    body = {
        "code": qa_code("QAAUTO-SC"), "name": "QAAUTO Probe", "componentType": "Earning",
        "calculationType": "FixedAmount", "statutoryType": "None", "effectiveFrom": QA_EFFECTIVE_FROM,
    }
    body.update(overrides)
    return body


@allure.title("PAY-SC-001 — a well-formed FixedAmount Earning component is created active")
@qa_cases("PAY-SC-001")
@pytest.mark.critical
def test_create_valid_component(component):
    assert component["isActive"] is True
    assert component["componentType"] == "Earning"
    assert component["calculationType"] == "FixedAmount"
    assert component["concurrencyVersion"] == 1


@allure.title("PAY-SC-002 — listing respects componentType/isActive filters and is paged")
@qa_cases("PAY-SC-002")
def test_list_filters_and_paging(admin_api_client, component):
    response = list_salary_components(admin_api_client, componentType="Earning", isActive="true", page=1, pageSize=200)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert {"items", "totalCount", "page", "pageSize", "totalPages", "hasNextPage", "hasPreviousPage"} <= set(data)
    assert all(item["componentType"] == "Earning" and item["isActive"] for item in data["items"])
    assert any(item["id"] == component["id"] for item in data["items"])


@allure.title("PAY-SC-002 — a small pageSize is honoured (paging is real, not a no-op)")
@qa_cases("PAY-SC-002")
def test_paging_honours_page_size(admin_api_client, component):
    data = list_salary_components(admin_api_client, page=1, pageSize=1).json()["data"]
    assert len(data["items"]) <= 1
    assert data["pageSize"] == 1
    if data["totalCount"] > 1:
        assert data["hasNextPage"] is True


@allure.title("PAY-SC-003 — get-by-id returns the full projection including ConcurrencyVersion")
@qa_cases("PAY-SC-003")
def test_get_by_id_projection(admin_api_client, component):
    response = get_salary_component(admin_api_client, component["id"])
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert data["id"] == component["id"]
    assert data["code"] == component["code"]
    assert "concurrencyVersion" in data


@allure.title("PAY-SC-004 — a blank Code or Name is rejected 400 with the field-specific message")
@qa_cases("PAY-SC-004")
@pytest.mark.critical
def test_blank_code_or_name_rejected(admin_api_client):
    blank_code = create_salary_component(admin_api_client, **_base(code=""))
    assert blank_code.status_code == 400, blank_code.text
    assert "Salary Component Code is required." in blank_code.text

    blank_name = create_salary_component(admin_api_client, **_base(name=""))
    assert blank_name.status_code == 400, blank_name.text
    assert "Salary Component Name is required." in blank_name.text


@allure.title("PAY-SC-005 — EffectiveTo before EffectiveFrom is rejected 400")
@qa_cases("PAY-SC-005")
def test_inverted_effective_dates_rejected(admin_api_client):
    response = create_salary_component(admin_api_client, **_base(effectiveFrom="2026-04-01", effectiveTo="2026-03-01"))
    assert response.status_code == 400, response.text
    assert "Effective To cannot be earlier than Effective From." in response.text


@allure.title("PAY-SC-006 — IsStatutory must agree with StatutoryType (both directions)")
@qa_cases("PAY-SC-006")
def test_statutory_type_must_match(admin_api_client):
    on_none = create_salary_component(admin_api_client, **_base(componentType="Deduction", isStatutory=True, statutoryType="None"))
    assert on_none.status_code == 400, on_none.text
    assert "Statutory Type must match IsStatutory." in on_none.text

    off_pf = create_salary_component(admin_api_client, **_base(componentType="Deduction", isStatutory=False, statutoryType="ProvidentFund"))
    assert off_pf.status_code == 400, off_pf.text
    assert "Statutory Type must match IsStatutory." in off_pf.text


@allure.title("PAY-SC-007 — an EmployerContribution component cannot affect net pay")
@qa_cases("PAY-SC-007")
def test_employer_contribution_cannot_affect_net(admin_api_client):
    response = create_salary_component(admin_api_client, **_base(componentType="EmployerContribution", affectsNetPay=True))
    assert response.status_code == 400, response.text
    assert "Employer contributions cannot affect employee net pay." in response.text


@allure.title("PAY-SC-008 — code is normalized to trimmed uppercase and is unique per tenant")
@qa_cases("PAY-SC-008")
def test_code_normalized_and_unique(admin_api_client):
    raw = qa_code("QAAUTO-SC")
    created = create_salary_component(admin_api_client, **_base(code=f"  {raw.lower()}  "))
    assert created.status_code in (200, 201), created.text
    component_id = created.json()["data"]["id"]
    try:
        assert created.json()["data"]["code"] == raw, "code was not normalised to trimmed uppercase"
        dup = create_salary_component(admin_api_client, **_base(code=raw))
        assert dup.status_code == 409, dup.text
        assert f"A salary component with code '{raw}' already exists." in dup.text
    finally:
        cleanup_component(admin_api_client, component_id)


@allure.title("PAY-SC-009 — a concurrent duplicate-code create race resolves to exactly one success")
@qa_cases("PAY-SC-009")
def test_concurrent_duplicate_code_race(admin_api_client):
    code = qa_code("QAAUTO-SC")
    body = _base(code=code, name="QAAUTO Race")

    def fire():
        return create_salary_component(admin_api_client, **body).status_code

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        statuses = list(pool.map(lambda _: fire(), range(4)))

    successes = [s for s in statuses if s in (200, 201)]
    conflicts = [s for s in statuses if s == 409]
    assert len(successes) == 1, f"expected exactly one success, got {statuses}"
    assert len(conflicts) == len(statuses) - 1, f"every loser must be a 409, got {statuses}"

    remaining = list_salary_components(admin_api_client, search=code, pageSize=200).json()["data"]["items"]
    matches = [c for c in remaining if c["code"] == code]
    assert len(matches) == 1, f"exactly one row must exist for {code}, found {len(matches)}"
    cleanup_component(admin_api_client, matches[0]["id"])


@allure.title("PAY-SC-010 — updating a nonexistent component is 404")
@qa_cases("PAY-SC-010")
def test_update_missing_is_404(admin_api_client):
    response = update_salary_component(admin_api_client, str(uuid.uuid4()), **_base())
    assert response.status_code == 404, response.text
    assert "Salary component not found." in response.text


@allure.title("PAY-SC-011 — an update with a stale ExpectedConcurrencyVersion is 409")
@qa_cases("PAY-SC-011")
@pytest.mark.critical
def test_stale_concurrency_version_on_update(admin_api_client, component):
    stale = component["concurrencyVersion"]
    first = update_salary_component(
        admin_api_client, component["id"], code=component["code"], name="QAAUTO updated",
        componentType="Earning", calculationType="FixedAmount", statutoryType="None",
        isActive=True, effectiveFrom=QA_EFFECTIVE_FROM, expectedConcurrencyVersion=stale)
    assert first.status_code == 200, first.text
    assert first.json()["data"]["concurrencyVersion"] == stale + 1

    replay = update_salary_component(
        admin_api_client, component["id"], code=component["code"], name="QAAUTO stale",
        componentType="Earning", calculationType="FixedAmount", statutoryType="None",
        isActive=True, effectiveFrom=QA_EFFECTIVE_FROM, expectedConcurrencyVersion=stale)
    assert replay.status_code == 409, replay.text
    assert "The salary component was changed by another user." in replay.text


@allure.title("PAY-SC-012 — renaming a component's code onto a sibling's code is 409")
@qa_cases("PAY-SC-012")
def test_update_code_collision(admin_api_client, component):
    other = make_component(admin_api_client)
    try:
        response = update_salary_component(
            admin_api_client, component["id"], code=other["code"], name=component["name"],
            componentType="Earning", calculationType="FixedAmount", statutoryType="None",
            isActive=True, effectiveFrom=QA_EFFECTIVE_FROM,
            expectedConcurrencyVersion=component["concurrencyVersion"])
        assert response.status_code == 409, response.text
        assert f"A salary component with code '{other['code']}' already exists." in response.text
    finally:
        cleanup_component(admin_api_client, other["id"])


@allure.title("PAY-SC-013 — deactivate is idempotent: a no-op writes no extra history row")
@qa_cases("PAY-SC-013")
def test_deactivate_idempotent_no_history(admin_api_client, component):
    first = deactivate_salary_component(admin_api_client, component["id"])
    assert first.status_code == 200 and first.json()["data"]["isActive"] is False, first.text
    baseline = len(get_salary_component_history(admin_api_client, component["id"]).json()["data"])

    second = deactivate_salary_component(admin_api_client, component["id"])
    assert second.status_code == 200 and second.json()["data"]["isActive"] is False, second.text
    after = len(get_salary_component_history(admin_api_client, component["id"]).json()["data"])
    assert after == baseline, "a no-op deactivate must not append a history row"


@allure.title("PAY-SC-014 — activate honours a stale expectedConcurrencyVersion (409)")
@qa_cases("PAY-SC-014")
def test_activate_stale_version(admin_api_client, component):
    deactivate_salary_component(admin_api_client, component["id"])
    stale = component["concurrencyVersion"]  # now stale, version advanced by the deactivate
    response = activate_salary_component(admin_api_client, component["id"])
    # sanity: a plain activate (no version) succeeds
    assert response.status_code == 200, response.text
    deactivate_salary_component(admin_api_client, component["id"])
    stale_attempt = admin_api_client.post(
        f"/api/payroll/salary-components/{component['id']}/activate",
        params={"expectedConcurrencyVersion": stale})
    assert stale_attempt.status_code == 409, stale_attempt.text
    assert "The salary component was changed by another user." in stale_attempt.text


@allure.title("PAY-SC-015 — history is append-only (Created + Updated) and 404 for an unknown id")
@qa_cases("PAY-SC-015")
def test_history_append_only_and_missing(admin_api_client, component):
    update_salary_component(
        admin_api_client, component["id"], code=component["code"], name="QAAUTO history",
        componentType="Earning", calculationType="FixedAmount", statutoryType="None",
        isActive=True, effectiveFrom=QA_EFFECTIVE_FROM,
        expectedConcurrencyVersion=component["concurrencyVersion"])
    history = get_salary_component_history(admin_api_client, component["id"])
    assert history.status_code == 200, history.text
    entries = history.json()["data"]
    assert len(entries) >= 2
    change_types = {e["changeType"] for e in entries}
    assert "Created" in change_types and "Updated" in change_types

    missing = get_salary_component_history(admin_api_client, str(uuid.uuid4()))
    assert missing.status_code == 404, missing.text
    assert "Salary component not found." in missing.text


@allure.title("PAY-SC-016 — a component id not in this tenant is 404, leaking no existence")
@qa_cases("PAY-SC-016")
@pytest.mark.critical
def test_foreign_component_id_is_404(admin_api_client):
    # A single-tenant environment cannot host a real Tenant-B id; a well-formed id that does not
    # belong to this tenant must be indistinguishable from a nonexistent one — 404, never 403/500.
    response = get_salary_component(admin_api_client, str(uuid.uuid4()))
    assert response.status_code == 404, response.text
    assert "Salary component not found." in response.text


@allure.title("PAY-SC — an unauthenticated caller is rejected 401 on the component surface")
@qa_cases("PAY-SC-002")
@pytest.mark.critical
def test_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert list_salary_components(anon).status_code == 401
