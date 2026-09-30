"""API regression suite for Module 10 — Salary Structures and Versions.

Automated from qa/10-payroll/cases/02-salary-structures-versions.yaml (PAY-SS-001..021). Every status
and message was confirmed live before writing.

CONFIRMED DEFECT surfaced here — F4 (PAY-SS-012, versioning-on-update): updating a structure with a
genuinely new, non-overlapping EffectiveFrom is documented to create a second version (200) but
returns HTTP 500 ("Could not save changes...") on the live server. Root cause is visible in
`SalaryStructureService.UpdateAsync` (Backend/HRMS.Application/Services/SalaryStructureService.cs:103):
it catches only `DbUpdateConcurrencyException`, not the general `DbUpdateException` the create path
guards (:66), so the version-insert save failure is unhandled. The check is written to assert the
correct contract (a new version, 200) and is marked `known_defect`, so it stays visible but is
deselected by `-m "not known_defect"` for a green gate. The overlapping-version guard (409) and the
same-version reconcile path (200) both work and are asserted normally.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.payroll_api import (
    add_salary_structure_component,
    create_salary_structure,
    deactivate_salary_structure,
    get_salary_structure,
    get_salary_structure_effective,
    get_salary_structure_history,
    list_salary_structures,
    remove_salary_structure_component,
    update_salary_structure,
)
from utils.allure_evidence import cr_refs, known_defect, qa_cases
from utils.payroll_data import (
    QA_EFFECTIVE_FROM,
    cleanup_component,
    cleanup_structure,
    make_component,
    qa_code,
)

pytestmark = [allure.feature("Payroll"), allure.story("Salary Structures"), pytest.mark.api, pytest.mark.regression]


def _row(component_id, sequence=1, **overrides):
    row = {
        "salaryComponentId": component_id, "sequence": sequence, "calculationType": "FixedAmount",
        "value": 1000, "isProratable": False, "isEditableAtEmployeeLevel": False, "isActive": True,
        "effectiveFrom": QA_EFFECTIVE_FROM,
    }
    row.update(overrides)
    return row


@pytest.fixture
def component(admin_api_client):
    row = make_component(admin_api_client)
    yield row
    cleanup_component(admin_api_client, row["id"])


@pytest.fixture
def second_component(admin_api_client):
    row = make_component(admin_api_client)
    yield row
    cleanup_component(admin_api_client, row["id"])


@pytest.fixture
def structure(admin_api_client, component, second_component):
    """A valid two-component structure: FixedAmount BASIC + Percentage-of-BASIC HRA."""
    response = create_salary_structure(
        admin_api_client, code=qa_code("QAAUTO-SS"), name="QAAUTO Structure",
        effectiveFrom=QA_EFFECTIVE_FROM, isActive=True,
        components=[
            _row(component["id"], 1),
            _row(second_component["id"], 2, calculationType="Percentage", value=40,
                 percentageOfComponentId=component["id"]),
        ])
    if response.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO structure ({response.status_code}): {response.text}")
    row = response.json()["data"]
    yield row
    cleanup_structure(admin_api_client, row["id"])


def _create(admin_api_client, component_id, components=None, **hdr):
    body = {"code": qa_code("QAAUTO-SS"), "name": "QAAUTO", "effectiveFrom": QA_EFFECTIVE_FROM,
            "isActive": True, "components": components if components is not None else [_row(component_id)]}
    body.update(hdr)
    return create_salary_structure(admin_api_client, **body)


@allure.title("PAY-SS-001 — a two-component (FixedAmount + Percentage-of-base) structure is created")
@qa_cases("PAY-SS-001")
@pytest.mark.critical
def test_create_two_component_structure(structure, component):
    assert structure["isActive"] is True
    assert structure["componentCount"] == 2
    assert any(c["salaryComponentId"] == component["id"] for c in structure["components"])


@allure.title("PAY-SS-002 — blank Code and inverted effective dates are each rejected 400")
@qa_cases("PAY-SS-002")
def test_header_validation(admin_api_client, component):
    blank = _create(admin_api_client, component["id"], code="")
    assert blank.status_code == 400 and "Salary Structure Code is required." in blank.text, blank.text
    inverted = _create(admin_api_client, component["id"], effectiveFrom="2026-06-01", effectiveTo="2026-01-01")
    assert inverted.status_code == 400 and "Effective To cannot be earlier than Effective From." in inverted.text, inverted.text


@allure.title("PAY-SS-003 — a duplicate structure code is 409")
@qa_cases("PAY-SS-003")
def test_duplicate_code(admin_api_client, structure, component):
    dup = _create(admin_api_client, component["id"], code=structure["code"])
    assert dup.status_code == 409, dup.text
    assert "already exists" in dup.text


@allure.title("PAY-SS-004 — an empty component list is rejected 400")
@qa_cases("PAY-SS-004")
def test_empty_components(admin_api_client, component):
    response = _create(admin_api_client, component["id"], components=[])
    assert response.status_code == 400, response.text
    assert "At least one Salary Component is required." in response.text


@allure.title("PAY-SS-005 — an empty-guid component (400) and a duplicate component (409)")
@qa_cases("PAY-SS-005")
def test_empty_guid_and_duplicate_component(admin_api_client, component):
    empty = _create(admin_api_client, component["id"],
                    components=[_row("00000000-0000-0000-0000-000000000000")])
    assert empty.status_code == 400 and "Every structure row must reference a Salary Component." in empty.text, empty.text
    dup = _create(admin_api_client, component["id"],
                  components=[_row(component["id"], 1), _row(component["id"], 2)])
    assert dup.status_code == 409 and "A Salary Component may appear only once in a structure version." in dup.text, dup.text


@allure.title("PAY-SS-006 — sequence values must be positive (seq=0) and unique (two components, same seq)")
@qa_cases("PAY-SS-006")
def test_sequence_positive_and_unique(admin_api_client, component, second_component):
    zero = _create(admin_api_client, component["id"], components=[_row(component["id"], 0)])
    assert zero.status_code == 400 and "Component sequence values must be positive and unique." in zero.text, zero.text
    same_seq = _create(admin_api_client, component["id"],
                       components=[_row(component["id"], 1), _row(second_component["id"], 1)])
    assert same_seq.status_code == 400 and "Component sequence values must be positive and unique." in same_seq.text, same_seq.text


@allure.title("PAY-SS-007 — a component's dates must stay inside the version window")
@qa_cases("PAY-SS-007")
def test_component_dates_within_version(admin_api_client, component):
    response = _create(admin_api_client, component["id"], effectiveFrom="2020-01-01", effectiveTo="2020-12-31",
                       components=[_row(component["id"], effectiveTo="2027-01-15")])
    assert response.status_code == 400, response.text
    assert "Component effective dates must remain within the structure version." in response.text


@allure.title("PAY-SS-008 — a referenced component must exist, be active, and belong to the tenant")
@qa_cases("PAY-SS-008")
def test_inactive_component_ref_rejected(admin_api_client):
    inactive = make_component(admin_api_client)
    from core.payroll_api import deactivate_salary_component
    deactivate_salary_component(admin_api_client, inactive["id"])
    try:
        response = _create(admin_api_client, inactive["id"], components=[_row(inactive["id"])])
        assert response.status_code == 400, response.text
        assert "Every Salary Component must exist, be active, and belong to this tenant." in response.text
    finally:
        cleanup_component(admin_api_client, inactive["id"])


@allure.title("PAY-SS-009 — a Percentage component cannot reference itself as its base")
@qa_cases("PAY-SS-009")
def test_percentage_self_reference(admin_api_client, component):
    response = _create(admin_api_client, component["id"],
                       components=[_row(component["id"], calculationType="Percentage", value=40,
                                        percentageOfComponentId=component["id"])])
    assert response.status_code == 400, response.text
    assert "Percentage base must reference another active Salary Component in this tenant." in response.text


@allure.title("PAY-SS-010 — the calculation-type field matrix rejects cross-type fields")
@qa_cases("PAY-SS-010")
def test_calculation_type_matrix(admin_api_client, component, second_component):
    fixed_pct = _create(admin_api_client, component["id"],
                        components=[_row(component["id"], 1, percentageOfComponentId=second_component["id"]),
                                    _row(second_component["id"], 2)])
    assert fixed_pct.status_code == 400, fixed_pct.text  # fixed cannot carry a percentage base
    assert "Fixed amount cannot include a percentage base or formula." in fixed_pct.text

    pct_150 = _create(admin_api_client, component["id"],
                      components=[_row(component["id"], 1),
                                  _row(second_component["id"], 2, calculationType="Percentage", value=150,
                                       percentageOfComponentId=component["id"])])
    assert pct_150.status_code == 400 and "Percentage must be between 0 and 100." in pct_150.text, pct_150.text

    formula_blank = _create(admin_api_client, component["id"],
                            components=[_row(component["id"], calculationType="Formula", value=None, formula="")])
    assert formula_blank.status_code == 400 and "Formula text is required." in formula_blank.text, formula_blank.text

    manual_value = _create(admin_api_client, component["id"],
                           components=[_row(component["id"], calculationType="Manual", value=5000)])
    assert manual_value.status_code == 400 and "Manual rows cannot include a calculated value." in manual_value.text, manual_value.text


@allure.title("PAY-SS-011 — MinimumAmount/MaximumAmount bounds are validated")
@qa_cases("PAY-SS-011")
def test_min_max_bounds(admin_api_client, component):
    negative = _create(admin_api_client, component["id"], components=[_row(component["id"], minimumAmount=-1)])
    assert negative.status_code == 400 and "Minimum and maximum amounts are invalid." in negative.text, negative.text
    inverted = _create(admin_api_client, component["id"],
                       components=[_row(component["id"], minimumAmount=5000, maximumAmount=1000)])
    assert inverted.status_code == 400 and "Minimum and maximum amounts are invalid." in inverted.text, inverted.text


@allure.title("PAY-SS-012 — an update whose new version overlaps an existing version is 409")
@qa_cases("PAY-SS-012")
def test_overlapping_version_rejected(admin_api_client, component):
    created = _create(admin_api_client, component["id"], effectiveFrom="2020-01-01", effectiveTo="2020-12-31",
                      components=[_row(component["id"], effectiveTo="2020-12-31")])
    assert created.status_code in (200, 201), created.text
    row = created.json()["data"]
    try:
        overlap = update_salary_structure(
            admin_api_client, row["id"], code=row["code"], name="QAAUTO", effectiveFrom="2020-06-01",
            effectiveTo="2020-09-30", isActive=True, expectedConcurrencyVersion=row["concurrencyVersion"],
            components=[_row(component["id"], effectiveFrom="2020-06-01", effectiveTo="2020-09-30")])
        assert overlap.status_code == 409, overlap.text
        assert "The effective salary structure version overlaps an existing version." in overlap.text
    finally:
        cleanup_structure(admin_api_client, row["id"])


@allure.title("PAY-SS-012 (F4) — updating with a new non-overlapping EffectiveFrom should create a second version")
@qa_cases("PAY-SS-012")
@known_defect("F4")
@pytest.mark.known_defect
def test_new_version_on_update_creates_second_version(admin_api_client, component):
    """CONFIRMED DEFECT F4: this documents the correct contract and currently FAILS — the endpoint
    returns HTTP 500 ("Could not save changes...") instead of 200. SalaryStructureService.UpdateAsync
    catches only DbUpdateConcurrencyException, not the DbUpdateException the create path guards, so
    the version-insert save failure is unhandled. Deselected from the green gate via
    `-m "not known_defect"`; it will pass once the update path handles the save failure."""
    created = _create(admin_api_client, component["id"], effectiveFrom="2020-01-01", effectiveTo="2020-12-31",
                      components=[_row(component["id"], effectiveTo="2020-12-31")])
    assert created.status_code in (200, 201), created.text
    row = created.json()["data"]
    try:
        response = update_salary_structure(
            admin_api_client, row["id"], code=row["code"], name="QAAUTO", effectiveFrom="2021-01-01",
            effectiveTo="2021-12-31", isActive=True, expectedConcurrencyVersion=row["concurrencyVersion"],
            components=[_row(component["id"], effectiveFrom="2021-01-01", effectiveTo="2021-12-31")])
        assert response.status_code == 200, (
            f"KNOWN DEFECT F4: versioning-on-update returned {response.status_code}: {response.text[:300]}")
        # Once fixed, a date inside the 2021 window must resolve the new version.
        v2 = get_salary_structure_effective(admin_api_client, row["id"], "2021-06-15")
        assert v2.status_code == 200, v2.text
    finally:
        cleanup_structure(admin_api_client, row["id"])


@allure.title("PAY-SS-013 — updating with a matching EffectiveFrom reconciles the same version (soft-deactivate)")
@qa_cases("PAY-SS-013")
def test_reconcile_same_version_soft_deactivates(admin_api_client, component, second_component):
    created = _create(admin_api_client, component["id"], effectiveFrom="2020-01-01", effectiveTo="2020-12-31",
                      components=[_row(component["id"], 1, effectiveTo="2020-12-31"),
                                  _row(second_component["id"], 2, effectiveTo="2020-12-31")])
    assert created.status_code in (200, 201), created.text
    row = created.json()["data"]
    try:
        response = update_salary_structure(
            admin_api_client, row["id"], code=row["code"], name="QAAUTO", effectiveFrom="2020-01-01",
            effectiveTo="2020-12-31", isActive=True, expectedConcurrencyVersion=row["concurrencyVersion"],
            components=[_row(component["id"], 1, effectiveTo="2020-12-31")])  # omit second_component
        assert response.status_code == 200, response.text
        components = response.json()["data"]["components"]
        omitted = [c for c in components if c["salaryComponentId"] == second_component["id"]]
        assert omitted and omitted[0]["isActive"] is False, "the omitted component should be soft-deactivated, not deleted"
    finally:
        cleanup_structure(admin_api_client, row["id"])


@allure.title("PAY-SS-014 — an update with a stale ExpectedConcurrencyVersion is 409")
@qa_cases("PAY-SS-014")
def test_stale_concurrency_on_update(admin_api_client, structure, component):
    response = update_salary_structure(
        admin_api_client, structure["id"], code=structure["code"], name="QAAUTO stale",
        effectiveFrom=QA_EFFECTIVE_FROM, isActive=True,
        expectedConcurrencyVersion=structure["concurrencyVersion"] + 99,
        components=[_row(component["id"])])
    assert response.status_code == 409, response.text
    assert "The salary structure was changed by another user." in response.text


@allure.title("PAY-SS-015 — deactivate is idempotent (no extra history row on a no-op)")
@qa_cases("PAY-SS-015")
def test_deactivate_idempotent(admin_api_client, structure):
    first = deactivate_salary_structure(admin_api_client, structure["id"])
    assert first.status_code == 200 and first.json()["data"]["isActive"] is False, first.text
    baseline = len(get_salary_structure_history(admin_api_client, structure["id"]).json()["data"])
    second = deactivate_salary_structure(admin_api_client, structure["id"])
    assert second.status_code == 200, second.text
    after = len(get_salary_structure_history(admin_api_client, structure["id"]).json()["data"])
    assert after == baseline


@allure.title("PAY-SS-016 — adding a component already in the version is rejected 409")
@qa_cases("PAY-SS-016")
def test_add_duplicate_component(admin_api_client, structure, component):
    response = add_salary_structure_component(
        admin_api_client, structure["id"], salaryComponentId=component["id"], sequence=9,
        calculationType="FixedAmount", value=500, isActive=True, effectiveFrom=QA_EFFECTIVE_FROM)
    assert response.status_code == 409, response.text
    # The duplicate is caught by component-set validation before the insert.
    assert "A Salary Component may appear only once in a structure version." in response.text


@allure.title("PAY-SS-016 (F5) — adding a genuinely new component to a structure should succeed")
@qa_cases("PAY-SS-016")
@known_defect("F5")
@pytest.mark.known_defect
def test_add_new_component_succeeds(admin_api_client, structure):
    """CONFIRMED DEFECT F5: adding a genuinely new (non-duplicate) component to a structure is
    documented to succeed but returns HTTP 500 on the live server — SalaryStructureService
    .AddComponentAsync (SalaryStructureService.cs:142) saves an insert-plus-concurrency-bump without
    catching the resulting DbUpdateConcurrencyException ('expected to affect 1 row … affected 0').
    The remove path (a pure update) is unaffected — see PAY-SS-017. This asserts the correct 201
    contract, so it currently fails and is deselected by `-m "not known_defect"`."""
    fresh = make_component(admin_api_client)
    try:
        added = add_salary_structure_component(
            admin_api_client, structure["id"], salaryComponentId=fresh["id"], sequence=9,
            calculationType="FixedAmount", value=500, isActive=True, effectiveFrom=QA_EFFECTIVE_FROM)
        assert added.status_code in (200, 201), (
            f"KNOWN DEFECT F5: adding a new component returned {added.status_code}: {added.text[:300]}")
    finally:
        cleanup_component(admin_api_client, fresh["id"])


@allure.title("PAY-SS-017 — removing an existing component is a soft delete (ComponentRemoved history)")
@qa_cases("PAY-SS-017")
def test_remove_component_soft_delete(admin_api_client, structure, second_component):
    # Remove one of the two components the structure was created with — a pure update, so this
    # exercises RemoveComponentAsync directly without depending on the broken add path (F5).
    structure_component_id = next(
        c["id"] for c in structure["components"] if c["salaryComponentId"] == second_component["id"])
    removed = remove_salary_structure_component(admin_api_client, structure["id"], structure_component_id)
    assert removed.status_code == 200, removed.text
    history = get_salary_structure_history(admin_api_client, structure["id"]).json()["data"]
    assert history[0]["changeType"] == "ComponentRemoved"


@allure.title("PAY-SS-018 — SelectVersion resolves inside the window and falls back to the latest active version")
@qa_cases("PAY-SS-018")
def test_select_version_resolution(admin_api_client, component):
    created = _create(admin_api_client, component["id"], effectiveFrom="2020-01-01", effectiveTo="2020-12-31",
                      components=[_row(component["id"], effectiveTo="2020-12-31")])
    row = created.json()["data"]
    try:
        inside = get_salary_structure_effective(admin_api_client, row["id"], "2020-03-15")
        assert inside.status_code == 200 and inside.json()["data"]["componentCount"] >= 1, inside.text
        far_future = get_salary_structure_effective(admin_api_client, row["id"], "2099-01-01")
        assert far_future.status_code == 200 and far_future.json()["data"]["componentCount"] >= 1, far_future.text
    finally:
        cleanup_structure(admin_api_client, row["id"])


@allure.title("PAY-SS-019 — history is append-only and records the Created change")
@qa_cases("PAY-SS-019")
def test_history_append_only(admin_api_client, structure):
    response = get_salary_structure_history(admin_api_client, structure["id"])
    assert response.status_code == 200, response.text
    entries = response.json()["data"]
    assert entries and any(e["changeType"] == "Created" for e in entries)


@allure.title("PAY-SS-020 — a structure id not in this tenant is 404")
@qa_cases("PAY-SS-020")
@pytest.mark.critical
def test_foreign_structure_id_is_404(admin_api_client):
    got = get_salary_structure(admin_api_client, str(uuid.uuid4()))
    assert got.status_code == 404 and "Salary structure not found." in got.text, got.text
    history = get_salary_structure_history(admin_api_client, str(uuid.uuid4()))
    assert history.status_code == 404, history.text


@allure.title("PAY-SS-021 — list search and get-by-id read paths")
@qa_cases("PAY-SS-021")
def test_list_search_and_get(admin_api_client, structure):
    listed = list_salary_structures(admin_api_client, search=structure["code"], page=1, pageSize=50)
    assert listed.status_code == 200, listed.text
    assert any(s["id"] == structure["id"] for s in listed.json()["data"]["items"])
    got = get_salary_structure(admin_api_client, structure["id"])
    assert got.status_code == 200 and got.json()["data"]["componentCount"] >= 1, got.text
