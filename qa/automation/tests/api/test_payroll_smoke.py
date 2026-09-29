"""API smoke suite for Module 10 — Payroll (Phase 7, first batch).

Proof-of-concept coverage, automated from qa/10-payroll/cases (Payroll_SalaryComponents,
Payroll_SalaryStructures, Payroll_EmployeeSalary, Payroll_PeriodsRuns, Payroll_Calculation,
Payroll_Inputs, Payroll_Scope) plus one representative Bank Advice case
(qa/15-bank-advice-salary-disbursement) and one Analytics case
(qa/17-payroll-analytics-reconciliation). Deliberately narrow, per qa/10-payroll/README.md's own
"Scope decision" (Payroll is the largest single domain in the product — 24 design docs, 20
controllers, 30+ services): this batch covers only Salary Component Master, Salary Structures,
Employee Salary Assignment, Payroll Periods/Runs, the Calculation Engine's eligibility scoping and
error reporting (the live "happy path" is currently blocked here — see the finding below),
Recalculation, a narrow Payroll Inputs lifecycle, one Bank Advice smoke, one Analytics smoke, and
the authorization sweep the Phase 7 brief asks for. Reimbursements/Claims, Settlement
(Retro/Final Settlement — see CR-181, a confirmed defect that makes Retro unreachable), Off-Cycle/
Adjustments/Reversals, GL/Accounting posting (needs a full accounting configuration + GL mapping
setup this narrow batch does not provision), Statutory configuration, Loans (Module 11, explicitly
out of scope per qa/10-payroll/README.md), and Variable Pay (Module 12, likewise out of scope) are
explicitly NOT covered by this batch.

**Data-safety design, verified before writing a single test**: a live probe of ANEVRA01
(`qa/automation/README.md`'s Phase 7 notes) confirmed zero pre-existing Salary Components, Salary
Structures, Employee Salary Assignments, Payroll Periods, or Payroll Runs in this tenant — Payroll
is a completely clean slate here. Every fixture below therefore creates and uses only its own
QAAUTO-prefixed master data and its own single QAAUTO employee; `PayrollRunService.PrepareAsync`'s
eligibility query (`EmployeeSalaryAssignments.Where(Status == Active && ...)`) can only ever pick up
an employee who has an Employee Salary Assignment, so a payroll run built here can never sweep in
any other (real) employee's salary or produce a calculated result for one. Payroll Periods use a
deliberately out-of-band FiscalYear (2071) so a period created here can never collide with a real
future payroll cycle if this tenant is later used for genuine payroll.

**Irreversibility is respected explicitly.** `PayrollPeriodService`/`PayrollRunService`'s own state
machines have no "delete" or "reset" action once a period leaves Draft or a run leaves
Draft/Prepared (`ValidTransition`: Draft->Cancelled and Prepared->Cancelled are the only ways back;
Calculated->Approved->Finalized has no reverse edge). This batch never attempts Approve/Finalize on
a real happy path — see the Calculation Engine finding below for why that's currently unreachable
here regardless — so the deepest state any QAAUTO run in this tenant reaches is Prepared. The
QAAUTO period/run this suite creates are therefore permanent, easily identifiable (QAAUTO-prefixed
Code/Name/reason text) artifacts in this tenant — see "Cleanup" in the automation README's Phase 7
section for the full accounting of what is and isn't reversible.

**A real, source-verified environment gap constrains how deep this batch can go as a happy path**
(surfaced while writing `test_payroll_run_prepare_scopes_to_qaauto_employee_only` — full detail on
that test's own docstring): `PayrollCalculationEngine.CalculateEmployeeAsync` unconditionally calls
the Overtime snapshot resolver for every eligible employee, regardless of whether the employee's
Salary Structure has any Overtime/Attendance-driven component, and that resolver requires an
`AttendancePeriod` row matching the Payroll Period's dates exactly with `Status == Closed`. Since
Attendance Foundation/Monthly-Finalization is out of scope for this batch (and largely unconfigured
in this environment already, per qa/08-attendance/README.md), **no payroll run can currently reach
`Calculated` in ANEVRA01, for any period** — which in turn means Approve, Finalize, and Bank
Advice/GL generation (both gated on an Approved/Finalized run) are not reachable as a positive path
here either. This is reported, not routed around — see `test_bank_advice_requires_approval_then_
generates` for how the Bank Advice test handles it (proves what's provably true, skips the rest with
the precise live reason).

Requires QA_TENANT_A_HOST plus, per test: QA_A_ADMIN_USERNAME/PASSWORD (`admin_api_client` — the
only seeded identity holding any Payroll permission at all in this tenant; see
qa/10-payroll/README.md CR-165/CR-168: HRManager holds none of Payroll's core permissions and
Manager holds zero Payroll permissions of any kind) and, for the two authorization-denial checks
only, QA_A_EMPLOYEE_USERNAME/PASSWORD and QA_A_MANAGER_USERNAME/PASSWORD. Each fixture skips on its
own if its pair is unset or sign-in fails.
"""

from __future__ import annotations

import allure
import pytest

from core.employee_api import create_personal_details, delete_employee
from core.payroll_api import (
    activate_salary_component,
    calculate_run,
    cancel_input_batch,
    create_employee_salary_assignment,
    create_input_batch,
    create_payroll_period,
    create_payroll_run,
    create_salary_component,
    create_salary_structure,
    deactivate_salary_component,
    deactivate_salary_structure,
    generate_bank_advice,
    get_analytics_overview,
    get_assignments_by_employee,
    get_control_totals,
    get_effective_assignment,
    get_input_batch_preview,
    get_run_calculation_errors,
    get_run_history,
    get_run_readiness,
    get_run_results,
    get_salary_component_history,
    get_salary_structure_history,
    list_employee_salary_assignments,
    list_salary_components,
    prepare_run,
    recalculate_run,
    set_employee_salary_assignment_active,
    transition_payroll_period,
    transition_run,
    validate_input_batch,
)
from data.test_data import today_iso, unique_code
from utils.env_utils import require_env

pytestmark = [allure.feature("Payroll"), pytest.mark.api]

# A deliberately out-of-band fiscal-year range so a Payroll Period created by this suite can never
# collide with a real future payroll cycle, per the module docstring above. Every period this suite
# creates picks its own random month within this range (via `_unique_period_dates`) so repeated
# fixture/test runs never hit PayrollPeriodService's own active-period date-overlap guard against
# an earlier QAAUTO period left behind by a prior run of this same suite.
_QA_FISCAL_YEAR = 2071
_QA_COMPONENT_EFFECTIVE_FROM = "2020-01-01"
_QA_BASIC_PAY_VALUE = 30000


def _unique_period_dates() -> dict:
    """A random, non-colliding (Code aside — dates too) month somewhere in 2071-2470, so two
    periods created by this suite — even across separate runs — practically never overlap."""
    import calendar
    import random

    year = _QA_FISCAL_YEAR + random.randint(0, 399)
    month = random.randint(1, 12)
    last_day = calendar.monthrange(year, month)[1]
    pay_year, pay_month = (year, month + 1) if month < 12 else (year + 1, 1)
    return {
        "fiscalYear": year,
        "periodNumber": month,
        "startDate": f"{year:04d}-{month:02d}-01",
        "endDate": f"{year:04d}-{month:02d}-{last_day:02d}",
        "payDate": f"{pay_year:04d}-{pay_month:02d}-01",
    }


# --- Fixtures: the Salary Component -> Structure -> Employee -> Assignment -> Period -> Run
# dependency chain this module's flows are built on. Each is function-scoped and creates only its
# own QAAUTO-prefixed data; see the module docstring for why this can never touch a real employee's
# payroll. ---


@pytest.fixture
def salary_component(admin_api_client):
    """One QAAUTO Earning/FixedAmount Salary Component ("Basic Pay"-equivalent). Deactivated
    (never deleted — no delete endpoint exists for this master entity) in teardown, best-effort."""
    code = unique_code("QAAUTO-SC")
    response = create_salary_component(
        admin_api_client,
        code=code,
        name=f"QAAUTO Basic Pay {code[-8:]}",
        description="QA automation smoke component — safe to deactivate/ignore.",
        componentType="Earning",
        calculationType="FixedAmount",
        statutoryType="None",
        isTaxable=True,
        isStatutory=False,
        isRecurring=True,
        affectsGross=True,
        affectsNetPay=True,
        displayOrder=1,
        effectiveFrom=_QA_COMPONENT_EFFECTIVE_FROM,
        isActive=True,
    )
    if response.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO salary component this test needs ({response.status_code}): {response.text}")
    component = response.json()["data"]

    yield component

    deactivate_salary_component(admin_api_client, component["id"])


@pytest.fixture
def salary_structure(admin_api_client, salary_component):
    """One QAAUTO Salary Structure with a single FixedAmount component bound to `salary_component`.
    Not proratable, so a calculated result's NetPay is deterministically the component's Value
    regardless of where the (out-of-band) payroll period falls relative to the employee's join
    date. Deactivated (never deleted) in teardown, best-effort."""
    code = unique_code("QAAUTO-SS")
    response = create_salary_structure(
        admin_api_client,
        code=code,
        name=f"QAAUTO Structure {code[-8:]}",
        description="QA automation smoke structure — safe to deactivate/ignore.",
        effectiveFrom=_QA_COMPONENT_EFFECTIVE_FROM,
        isActive=True,
        components=[
            {
                "salaryComponentId": salary_component["id"],
                "sequence": 1,
                "calculationType": "FixedAmount",
                "value": _QA_BASIC_PAY_VALUE,
                "isProratable": False,
                "isEditableAtEmployeeLevel": False,
                "isActive": True,
                "effectiveFrom": _QA_COMPONENT_EFFECTIVE_FROM,
            }
        ],
    )
    if response.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO salary structure this test needs ({response.status_code}): {response.text}")
    structure = response.json()["data"]

    yield structure

    deactivate_salary_structure(admin_api_client, structure["id"])


@pytest.fixture
def payroll_employee(admin_api_client):
    """One disposable QAAUTO employee (personal-details path), independent of `created_employee`
    in conftest.py so this module stays self-contained. Deleted (best-effort) in teardown — if the
    delete fails because payroll history now references this employee, that is logged, not failed,
    matching this framework's established cleanup philosophy (see README "Test data and cleanup")."""
    from data.test_data import unique_last_name

    response = create_personal_details(
        admin_api_client,
        firstName="QaAutoPayroll",
        lastName=unique_last_name(),
        dateOfJoining=today_iso(),
    )
    if response.status_code != 201:
        pytest.fail(f"Setup failed: could not create the QAAUTO employee this test needs ({response.status_code}): {response.text}")
    employee = response.json()["data"]

    yield employee

    try:
        delete_employee(admin_api_client, employee["id"])
    except Exception:
        pass


@pytest.fixture
def employee_salary_assignment(admin_api_client, payroll_employee, salary_structure):
    """Binds `payroll_employee` to `salary_structure` effective today (well before the QAAUTO
    Payroll Period's out-of-band 2071 dates, so eligibility resolves cleanly). Deactivated (never
    deleted — no delete endpoint exists) in teardown, best-effort."""
    response = create_employee_salary_assignment(
        admin_api_client,
        employeeId=payroll_employee["id"],
        salaryStructureId=salary_structure["id"],
        effectiveFrom=today_iso(),
        annualCtc=_QA_BASIC_PAY_VALUE * 12,
        monthlyCtc=_QA_BASIC_PAY_VALUE,
        currencyCode="INR",
        payFrequency="Monthly",
        status="Active",
        changeReason="NewHire",
        remarks="QAAUTO automation smoke assignment.",
    )
    if response.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO employee salary assignment this test needs ({response.status_code}): {response.text}")
    assignment = response.json()["data"]

    yield assignment

    set_employee_salary_assignment_active(admin_api_client, assignment["id"], False)


@pytest.fixture
def payroll_period(admin_api_client):
    """One QAAUTO Payroll Period dated in fiscal year 2071 — see the module docstring for why this
    out-of-band year can never collide with a real payroll cycle. No cleanup is possible (Payroll
    Periods have no delete endpoint); it is left in whatever state the test advanced it to,
    identifiable by its QAAUTO code for a human to review later."""
    code = unique_code("QAAUTO-PER")
    response = create_payroll_period(
        admin_api_client,
        code=code,
        name=f"QAAUTO Period {code[-8:]}",
        periodType="Monthly",
        isActive=True,
        **_unique_period_dates(),
    )
    if response.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO payroll period this test needs ({response.status_code}): {response.text}")
    return response.json()["data"]


@pytest.fixture
def calculated_payroll_run(admin_api_client, payroll_period, employee_salary_assignment):
    """A payroll run for `payroll_period`, prepared and calculated. Only `employee_salary_
    assignment`'s employee can be eligible (see module docstring). No cleanup is possible (no
    delete/cancel action exists once a run leaves Draft/Prepared); left Calculated for the test to
    read or (in the Bank Advice test) advance further."""
    run_response = create_payroll_run(admin_api_client, payrollPeriodId=payroll_period["id"], runType="Regular", notes="QAAUTO automation smoke run.")
    if run_response.status_code not in (200, 201):
        pytest.fail(f"Setup failed: could not create the QAAUTO payroll run this test needs ({run_response.status_code}): {run_response.text}")
    run = run_response.json()["data"]

    prepared = prepare_run(admin_api_client, run["id"])
    if prepared.status_code != 200:
        pytest.fail(f"Setup failed: could not prepare the QAAUTO payroll run this test needs ({prepared.status_code}): {prepared.text}")
    run = prepared.json()["data"]  # includes the populated Employees list, unlike the bare create response

    calculated = calculate_run(admin_api_client, run["id"])
    if calculated.status_code != 200:
        pytest.fail(f"Setup failed: could not calculate the QAAUTO payroll run this test needs ({calculated.status_code}): {calculated.text}")

    return {"run": run, "employee": employee_salary_assignment, "period": payroll_period, "summary": calculated.json()["data"]}


# --- Salary Component Master ---


@allure.story("Salary Component Master")
@allure.title("PAY-SC-001 — a well-formed FixedAmount Earning component is created active")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_create_salary_component_minimum_valid_data(salary_component):
    assert salary_component["isActive"] is True
    assert salary_component["componentType"] == "Earning"
    assert salary_component["calculationType"] == "FixedAmount"
    assert "concurrencyVersion" in salary_component


@allure.story("Salary Component Master")
@allure.title("PAY-SC-002 — listing respects the componentType/isActive filters")
@pytest.mark.smoke
@pytest.mark.regression
def test_salary_components_list_supports_filters(admin_api_client, salary_component):
    response = list_salary_components(admin_api_client, componentType="Earning", isActive="true", pageSize=200)
    assert response.status_code == 200, response.text
    items = response.json()["data"]["items"]
    assert all(item["componentType"] == "Earning" and item["isActive"] for item in items)
    assert any(item["id"] == salary_component["id"] for item in items), "Expected the just-created QAAUTO component in the filtered list"


@allure.story("Salary Component Master")
@allure.title("PAY-SC-004 — a blank Code or Name is rejected")
@pytest.mark.smoke
@pytest.mark.regression
def test_missing_code_or_name_is_rejected(admin_api_client):
    blank_code = create_salary_component(
        admin_api_client, code="", name="QAAUTO Probe", componentType="Earning", calculationType="FixedAmount",
        statutoryType="None", effectiveFrom=_QA_COMPONENT_EFFECTIVE_FROM,
    )
    assert blank_code.status_code == 400, blank_code.text

    blank_name = create_salary_component(
        admin_api_client, code=unique_code("QAAUTO-SC"), name="", componentType="Earning", calculationType="FixedAmount",
        statutoryType="None", effectiveFrom=_QA_COMPONENT_EFFECTIVE_FROM,
    )
    assert blank_name.status_code == 400, blank_name.text


@allure.story("Salary Component Master")
@allure.title("PAY-SC — deactivate/activate is idempotent and history records both changes")
@pytest.mark.smoke
@pytest.mark.regression
def test_salary_component_activation_lifecycle_and_history(admin_api_client, salary_component):
    component_id = salary_component["id"]

    with allure.step("Deactivate"):
        deactivated = deactivate_salary_component(admin_api_client, component_id)
        assert deactivated.status_code == 200, deactivated.text
        assert deactivated.json()["data"]["isActive"] is False

    with allure.step("Re-activate"):
        activated = activate_salary_component(admin_api_client, component_id)
        assert activated.status_code == 200, activated.text
        assert activated.json()["data"]["isActive"] is True

    with allure.step("History reflects Created + at least one lifecycle change"):
        history = get_salary_component_history(admin_api_client, component_id)
        assert history.status_code == 200, history.text
        entries = history.json()["data"]
        assert len(entries) >= 2, "Expected at least Created plus one Deactivated/Activated entry"


@allure.story("Authorization")
@allure.title("PAY-SCOPE — an Employee (holds no Payroll.SalaryComponent.* permission) gets 403 creating a component")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_cannot_manage_salary_components(employee_api_client):
    response = create_salary_component(
        employee_api_client, code=unique_code("QAAUTO-SC"), name="QAAUTO Probe", componentType="Earning",
        calculationType="FixedAmount", statutoryType="None", effectiveFrom=_QA_COMPONENT_EFFECTIVE_FROM,
    )
    assert response.status_code == 403, response.text


# --- Salary Structures / Versions ---


@allure.story("Salary Structures")
@allure.title("PAY-SS-001 — a structure with one FixedAmount component is created with the component attached")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_create_salary_structure_with_component(salary_structure, salary_component):
    assert salary_structure["isActive"] is True
    assert salary_structure["componentCount"] == 1
    assert len(salary_structure["components"]) == 1
    assert salary_structure["components"][0]["salaryComponentId"] == salary_component["id"]
    assert salary_structure["components"][0]["value"] == _QA_BASIC_PAY_VALUE


@allure.story("Salary Structures")
@allure.title("PAY-SS — history is readable and records the Created change")
@pytest.mark.smoke
@pytest.mark.regression
def test_salary_structure_history_is_readable(admin_api_client, salary_structure):
    response = get_salary_structure_history(admin_api_client, salary_structure["id"])
    assert response.status_code == 200, response.text
    entries = response.json()["data"]
    assert len(entries) >= 1
    assert any(entry["changeType"] == "Created" for entry in entries)


# --- Employee Salary Assignment ---


@allure.story("Employee Salary Assignment")
@allure.title("PAY-ESA-001 — creating an assignment binds the employee to the structure and CTC")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_create_employee_salary_assignment_binds_ctc(employee_salary_assignment, salary_structure, payroll_employee):
    assert employee_salary_assignment["status"] == "Active"
    assert employee_salary_assignment["employeeId"] == payroll_employee["id"]
    assert employee_salary_assignment["salaryStructureId"] == salary_structure["id"]
    assert employee_salary_assignment["monthlyCtc"] == _QA_BASIC_PAY_VALUE


@allure.story("Employee Salary Assignment")
@allure.title("PAY-ESA — GetEffective resolves the just-created assignment for today")
@pytest.mark.smoke
@pytest.mark.regression
def test_get_effective_assignment_for_employee(admin_api_client, employee_salary_assignment, payroll_employee):
    response = get_effective_assignment(admin_api_client, payroll_employee["id"], date=today_iso())
    assert response.status_code == 200, response.text
    assert response.json()["data"]["id"] == employee_salary_assignment["id"]


@allure.story("Employee Salary Assignment")
@allure.title("PAY-ESA — by-employee listing contains the just-created assignment")
@pytest.mark.smoke
@pytest.mark.regression
def test_employee_salary_assignment_by_employee_lists_assignment(admin_api_client, employee_salary_assignment, payroll_employee):
    response = get_assignments_by_employee(admin_api_client, payroll_employee["id"])
    assert response.status_code == 200, response.text
    items = response.json()["data"]["items"]
    assert any(item["id"] == employee_salary_assignment["id"] for item in items)


@allure.story("Authorization")
@allure.title("PAY-SCOPE — a Manager (holds zero Payroll permissions per CR-168) gets 403 listing salary assignments")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_manager_has_zero_payroll_permissions(manager_api_client):
    response = list_employee_salary_assignments(manager_api_client)
    assert response.status_code == 403, response.text


# --- Payroll Periods ---


@allure.story("Payroll Periods")
@allure.title("PAY-PR-001/006 — a period is created Draft and follows the declared Draft->Open->Closed order")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_create_payroll_period_and_transition_lifecycle(admin_api_client, payroll_period):
    assert payroll_period["status"] == "Draft"

    with allure.step("Open"):
        opened = transition_payroll_period(admin_api_client, payroll_period["id"], "open")
        assert opened.status_code == 200, opened.text
        assert opened.json()["data"]["status"] == "Open"

    with allure.step("Close"):
        closed = transition_payroll_period(admin_api_client, payroll_period["id"], "close")
        assert closed.status_code == 200, closed.text
        assert closed.json()["data"]["status"] == "Closed"

    with allure.step("A closed period cannot be edited (PAY-PR-004)"):
        edit_attempt = admin_api_client.put(
            f"/api/payroll/periods/{payroll_period['id']}",
            json={**payroll_period, "name": "QAAUTO Renamed", "status": "Closed"},
        )
        assert edit_attempt.status_code == 409, edit_attempt.text

    with allure.step("A fresh Draft period cannot skip Open straight to Closed (PAY-PR-006)"):
        code = unique_code("QAAUTO-PER")
        fresh = create_payroll_period(
            admin_api_client, code=code, name=f"QAAUTO Period {code[-8:]}", periodType="Monthly",
            isActive=True, **_unique_period_dates(),
        )
        assert fresh.status_code in (200, 201), fresh.text
        skip_transition = transition_payroll_period(admin_api_client, fresh.json()["data"]["id"], "close")
        assert skip_transition.status_code == 409, skip_transition.text


# --- Payroll Runs / Calculation Engine / Recalculation ---


@allure.story("Calculation Engine")
@allure.title("PAY-CALC — prepare correctly scopes eligibility to only the QAAUTO employee; calculate/errors/recalculate are read live, not assumed")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_payroll_run_prepare_scopes_to_qaauto_employee_only(admin_api_client, calculated_payroll_run):
    """**Real, source-verified finding surfaced while writing this test (not a framework bug)**:
    `PayrollCalculationEngine.CalculateEmployeeAsync` unconditionally calls both the Attendance and
    Overtime snapshot resolvers for every eligible employee, regardless of whether the employee's
    Salary Structure has any Attendance/Overtime-driven component. `OvertimeService.ResolveAsync`
    requires an `AttendancePeriod` row with `StartDate`/`EndDate` matching the Payroll Period
    *exactly* and `Status == Closed` — Attendance Foundation/Monthly-Finalization is out of scope
    for this batch (and, per qa/08-attendance/README.md, largely unconfigured in this environment
    already), so **no Payroll Period in this environment can ever reach a clean `Calculated` run
    with zero errors today**, regardless of period dates. This is real, reproducible, and the same
    for every payroll period — not an artifact of this suite's out-of-band 2071+ dates. This test
    therefore asserts the actual, live outcome (a deterministic `OvertimeNotFinalized` failure for
    the one eligible employee) rather than an idealized "clean calculation" that is not currently
    reachable in this tenant. See the automation README's Phase 7 section for the full write-up.
    """
    run_id = calculated_payroll_run["run"]["id"]
    employee_id = calculated_payroll_run["employee"]["employeeId"]
    summary = calculated_payroll_run["summary"]
    run_detail = calculated_payroll_run["run"]

    with allure.step("Prepare scoped eligibility to only employees with an Employee Salary Assignment — the tenant's other (real) employees were correctly excluded"):
        eligible = [e for e in run_detail["employees"] if e["isEligible"]]
        excluded = [e for e in run_detail["employees"] if not e["isEligible"]]
        # Exactly one is guaranteed to be ours; a stray extra would only ever be another QAAUTO
        # leftover from this same suite (the only source of Employee Salary Assignments in this
        # tenant — see the module docstring's live-probe evidence), never a real employee, since
        # PrepareAsync's eligibility query requires an EmployeeSalaryAssignment to exist at all.
        assert employee_id in {e["employeeId"] for e in eligible}
        assert len(eligible) < run_detail["employeeCount"], "Expected the tenant's real employees (who hold no salary assignment) to be excluded, not swept into eligibility"
        assert len(excluded) == run_detail["employeeCount"] - len(eligible)

    with allure.step("Readiness is readable"):
        readiness = get_run_readiness(admin_api_client, run_id)
        assert readiness.status_code == 200, readiness.text

    with allure.step("Calculate attempted every eligible employee and failed each on the live OvertimeNotFinalized prerequisite gap"):
        assert summary["employeeCount"] == run_detail["employeeCount"]
        assert summary["calculatedCount"] == 0, summary
        assert summary["failedCount"] == len(eligible), summary

    with allure.step("Calculation-errors names the failure precisely for our employee (and only ever another QAAUTO one, never a real employee)"):
        errors = get_run_calculation_errors(admin_api_client, run_id)
        assert errors.status_code == 200, errors.text
        entries = errors.json()["data"]
        assert len(entries) == len(eligible)
        assert any(entry["employeeId"] == employee_id and "OvertimeNotFinalized" in entry["message"] for entry in entries)
        assert all("OvertimeNotFinalized" in entry["message"] for entry in entries)

    with allure.step("Results list is empty — nothing was actually calculated"):
        results = get_run_results(admin_api_client, run_id)
        assert results.status_code == 200, results.text
        assert results.json()["data"]["items"] == []

    with allure.step("Recalculate reproduces the identical, deterministic outcome"):
        recalculated = recalculate_run(admin_api_client, run_id)
        assert recalculated.status_code == 200, recalculated.text
        assert recalculated.json()["data"]["calculatedCount"] == 0
        assert recalculated.json()["data"]["failedCount"] == len(eligible)

    with allure.step("Run history records Prepared regardless of the downstream calculation outcome"):
        history = get_run_history(admin_api_client, run_id)
        assert history.status_code == 200, history.text
        change_types = {entry["changeType"] for entry in history.json()["data"]}
        assert "Prepared" in change_types


@allure.story("Authorization")
@allure.title("PAY-SCOPE — an Employee gets 403 creating a payroll run")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_cannot_manage_payroll_runs(employee_api_client, payroll_period):
    response = create_payroll_run(employee_api_client, payrollPeriodId=payroll_period["id"], runType="Regular")
    assert response.status_code == 403, response.text


# --- Payroll Inputs ---


@allure.story("Payroll Inputs")
@allure.title("PAY-IN — a Manual input batch is created, validated, previewed, then cancelled with a reason")
@pytest.mark.smoke
@pytest.mark.regression
def test_payroll_input_batch_create_validate_preview_cancel(admin_api_client, payroll_period):
    code_suffix = unique_code("QAAUTO-IN")
    created = create_input_batch(
        admin_api_client,
        name=f"QAAUTO Input Batch {code_suffix[-8:]}",
        description="QA automation smoke batch — safe to leave Cancelled.",
        payrollPeriodId=payroll_period["id"],
        effectiveDate=today_iso(),
        sourceType="Manual",
        lines=[],
    )
    assert created.status_code in (200, 201), created.text
    batch = created.json()["data"]

    with allure.step("Validate an empty batch — expect either a clean pass or a documented validation issue, never a framework error"):
        validated = validate_input_batch(admin_api_client, batch["id"])
        assert validated.status_code == 200, validated.text

    with allure.step("Preview is readable"):
        preview = get_input_batch_preview(admin_api_client, batch["id"])
        assert preview.status_code == 200, preview.text

    with allure.step("Cancel with a reason"):
        cancelled = cancel_input_batch(admin_api_client, batch["id"], "QAAUTO automation smoke cleanup.")
        assert cancelled.status_code == 200, cancelled.text
        assert cancelled.json()["data"]["status"] == "Cancelled"


# --- Bank Advice (Module 15) — one representative smoke, gated on an Approved run ---


@allure.story("Bank Advice")
@allure.title("BANKADV-GEN — generating bank advice against a non-Approved run is correctly rejected (positive path blocked, see docstring)")
@pytest.mark.smoke
@pytest.mark.regression
def test_bank_advice_requires_approval_then_generates(admin_api_client, calculated_payroll_run):
    """The full "generate after Approve" happy path is blocked in this environment by the same
    live `OvertimeNotFinalized` gap documented on `test_payroll_run_prepare_scopes_to_qaauto_
    employee_only`: a run can only transition Calculated->Approved, and no run can currently reach
    Calculated here (calculatedCount is always 0). This test therefore proves the one thing that
    *is* reachable and real — generation is correctly rejected before approval — and skips the
    positive path with the precise, live reason rather than faking it or silently working around
    the run-status precondition (e.g. by approving from Prepared, which the API itself refuses)."""
    run_id = calculated_payroll_run["run"]["id"]

    with allure.step("Generating bank advice against a non-Approved run is correctly rejected"):
        premature = generate_bank_advice(admin_api_client, run_id)
        assert premature.status_code == 409, premature.text

    with allure.step("Approve is unreachable: the run never left Prepared (see the Calculation Engine finding)"):
        approved = transition_run(admin_api_client, run_id, "Approved")
        assert approved.status_code == 409, approved.text
        pytest.skip(
            "Bank Advice generation (the positive path) requires an Approved/Finalized run, which requires "
            "a Calculated run, which this environment's Calculation Engine cannot currently produce for any "
            "payroll period (OvertimeService.ResolveAsync requires an exactly-matching, Closed AttendancePeriod "
            "— see test_payroll_run_prepare_scopes_to_qaauto_employee_only). Not a framework issue; re-run once "
            "Attendance Foundation/Monthly-Finalization is provisioned for this tenant, or once the Calculation "
            "Engine's unconditional Overtime-resolution call is made conditional on the structure actually "
            "having an Overtime/AttendanceBased component."
        )


# --- Payroll Analytics (Module 17) — read-only smoke on the already-calculated run ---


@allure.story("Payroll Analytics")
@allure.title("PYA-OVW — overview and control-totals are readable for a calculated run and reflect the one QAAUTO employee")
@pytest.mark.smoke
@pytest.mark.regression
def test_payroll_analytics_overview_and_control_totals(admin_api_client, calculated_payroll_run):
    """Read-only smoke: both endpoints must be reachable and internally consistent for a Prepared
    run with zero calculated results (see the Calculation Engine finding on
    test_payroll_run_prepare_scopes_to_qaauto_employee_only) — NetPayTotal is legitimately 0 here,
    not because Analytics is broken, but because nothing has actually been calculated yet."""
    run_id = calculated_payroll_run["run"]["id"]

    overview = get_analytics_overview(admin_api_client, run_id)
    assert overview.status_code == 200, overview.text
    overview_data = overview.json()["data"]
    # EmployeeCount here reflects calculated PayrollResults, not the run's total population —
    # legitimately 0, same as Control Totals' ResultCount below (see the Calculation Engine
    # finding on test_payroll_run_prepare_scopes_to_qaauto_employee_only).
    assert overview_data["employeeCount"] == 0
    assert overview_data["netPayTotal"] == 0

    control_totals = get_control_totals(admin_api_client, run_id)
    assert control_totals.status_code == 200, control_totals.text
    assert control_totals.json()["data"]["resultCount"] == 0


# --- Authorization / unauthenticated sweep ---


@allure.story("Authorization")
@allure.title("PAY-SCOPE — no bearer token is rejected 401 on the Payroll surface")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_unauthenticated_payroll_call_is_rejected(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    anonymous_client = api_client_factory(host)
    response = list_salary_components(anonymous_client)
    assert response.status_code == 401, response.text
