"""Payroll module API helpers (Module 10, plus the Loans/Variable-Pay/Bank-Advice/GL/Analytics
areas covered by this Phase 7 smoke batch — see qa/10-payroll, qa/11-loans-advances,
qa/12-bonus-variable-pay, qa/15-bank-advice-salary-disbursement, qa/16-gl-accounting-integration,
qa/17-payroll-analytics-reconciliation).

Every function takes an already-authenticated `ApiClient` (see `conftest.py`'s `admin_api_client`)
and a plain `dict`/kwargs body, and returns the raw `requests.Response` — assertions stay in the
tests, these are just the wire calls, named after the endpoints they hit. Route prefixes and
permission gates below were confirmed by reading the controllers directly, not assumed from any
design doc:

- `SalaryComponentsController`      -> api/payroll/salary-components        (Payroll.SalaryComponent.*)
- `SalaryStructuresController`      -> api/payroll/salary-structures        (Payroll.SalaryStructure.*)
- `EmployeeSalaryAssignmentsController` -> api/payroll/employee-salary-assignments (Payroll.EmployeeSalary.*)
- `PayrollPeriodsController`        -> api/payroll/periods                  (Payroll.Period.*)
- `PayrollRunsController`           -> api/payroll/runs                    (Payroll.Run.*)
- `StatutoryPayrollController`      -> api/payroll/runs/{runId}/results/{employeeId}/statutory (Payroll.Statutory.View)
- `PayrollInputsController`         -> api/payroll/input-batches            (PayrollInput.*)
- `PayrollLoansController`          -> api/payroll/loan-products, api/payroll/loans (Payroll.Loans.*)
- `VariablePayController`           -> api/payroll/variable-pay-plans, -awards (Payroll.VariablePay.*)
- `BankAdviceController`            -> api/payroll/bank-advice              (Payroll.BankAdvice.*)
- `PayrollAccountingController`     -> api/payroll/accounting               (Payroll.Accounting.*)
- `PayrollAnalyticsController`/`PayrollReconciliationController` -> api/payroll/analytics (Payroll.Analytics.View / Reconciliation.*)

Per qa/10-payroll/README.md (CR-165/CR-168, reconfirmed against the current
`SeedData.RolePermissionMap`): the seeded `Employee` role holds only `Payroll.Adjustments.View`/
`ViewHistory` and `Payroll.TaxDeclaration.ViewOwn`/`ManageOwn` — none of the endpoints wrapped here
— and the seeded `Manager` role holds zero Payroll permissions of any kind. Only
`SuperAdmin`/`TenantAdmin` (the configured QA Admin identity) can reach this module's functional
surface; `employee_api_client`/`manager_api_client` are used in this suite only for the
authorization-denial checks, where a 403 is the expected, in-scope outcome.
"""

from __future__ import annotations

import allure

from core.api_client import ApiClient


# --- Salary Component Master ---


@allure.step("Create salary component")
def create_salary_component(client: ApiClient, **fields):
    return client.post("/api/payroll/salary-components", json=fields)


@allure.step("List salary components")
def list_salary_components(client: ApiClient, **query):
    return client.get("/api/payroll/salary-components", params=query)


@allure.step("Get salary component {component_id}")
def get_salary_component(client: ApiClient, component_id: str):
    return client.get(f"/api/payroll/salary-components/{component_id}")


@allure.step("Update salary component {component_id}")
def update_salary_component(client: ApiClient, component_id: str, **fields):
    return client.put(f"/api/payroll/salary-components/{component_id}", json=fields)


@allure.step("Activate salary component {component_id}")
def activate_salary_component(client: ApiClient, component_id: str):
    return client.post(f"/api/payroll/salary-components/{component_id}/activate")


@allure.step("Deactivate salary component {component_id}")
def deactivate_salary_component(client: ApiClient, component_id: str):
    return client.post(f"/api/payroll/salary-components/{component_id}/deactivate")


@allure.step("Get salary component history {component_id}")
def get_salary_component_history(client: ApiClient, component_id: str):
    return client.get(f"/api/payroll/salary-components/{component_id}/history")


# --- Salary Structures / Versions ---


@allure.step("Create salary structure")
def create_salary_structure(client: ApiClient, **fields):
    return client.post("/api/payroll/salary-structures", json=fields)


@allure.step("List salary structures")
def list_salary_structures(client: ApiClient, **query):
    return client.get("/api/payroll/salary-structures", params=query)


@allure.step("Get salary structure {structure_id}")
def get_salary_structure(client: ApiClient, structure_id: str):
    return client.get(f"/api/payroll/salary-structures/{structure_id}")


@allure.step("Activate salary structure {structure_id}")
def activate_salary_structure(client: ApiClient, structure_id: str):
    return client.post(f"/api/payroll/salary-structures/{structure_id}/activate")


@allure.step("Deactivate salary structure {structure_id}")
def deactivate_salary_structure(client: ApiClient, structure_id: str):
    return client.post(f"/api/payroll/salary-structures/{structure_id}/deactivate")


@allure.step("Get salary structure history {structure_id}")
def get_salary_structure_history(client: ApiClient, structure_id: str):
    return client.get(f"/api/payroll/salary-structures/{structure_id}/history")


# --- Employee Salary Assignment ---


@allure.step("Create employee salary assignment")
def create_employee_salary_assignment(client: ApiClient, **fields):
    return client.post("/api/payroll/employee-salary-assignments", json=fields)


@allure.step("List employee salary assignments")
def list_employee_salary_assignments(client: ApiClient, **query):
    return client.get("/api/payroll/employee-salary-assignments", params=query)


@allure.step("Get employee salary assignments by employee {employee_id}")
def get_assignments_by_employee(client: ApiClient, employee_id: str, **query):
    return client.get(f"/api/payroll/employee-salary-assignments/by-employee/{employee_id}", params=query)


@allure.step("Get effective employee salary assignment for {employee_id}")
def get_effective_assignment(client: ApiClient, employee_id: str, **query):
    return client.get(f"/api/payroll/employee-salary-assignments/by-employee/{employee_id}/effective", params=query)


@allure.step("Set employee salary assignment {assignment_id} active={active}")
def set_employee_salary_assignment_active(client: ApiClient, assignment_id: str, active: bool, **query):
    action = "activate" if active else "deactivate"
    return client.post(f"/api/payroll/employee-salary-assignments/{assignment_id}/{action}", params=query or None)


@allure.step("Get employee salary assignment history {assignment_id}")
def get_employee_salary_assignment_history(client: ApiClient, assignment_id: str):
    return client.get(f"/api/payroll/employee-salary-assignments/{assignment_id}/history")


# --- Payroll Periods ---


@allure.step("Create payroll period")
def create_payroll_period(client: ApiClient, **fields):
    return client.post("/api/payroll/periods", json=fields)


@allure.step("List payroll periods")
def list_payroll_periods(client: ApiClient, **query):
    return client.get("/api/payroll/periods", params=query)


@allure.step("Get payroll period {period_id}")
def get_payroll_period(client: ApiClient, period_id: str):
    return client.get(f"/api/payroll/periods/{period_id}")


@allure.step("Transition payroll period {period_id} to {action_name}")
def transition_payroll_period(client: ApiClient, period_id: str, action_name: str, **query):
    return client.post(f"/api/payroll/periods/{period_id}/{action_name}", params=query or None)


@allure.step("Get payroll period history {period_id}")
def get_payroll_period_history(client: ApiClient, period_id: str):
    return client.get(f"/api/payroll/periods/{period_id}/history")


# --- Payroll Runs / Calculation ---


@allure.step("Create payroll run")
def create_payroll_run(client: ApiClient, **fields):
    return client.post("/api/payroll/runs", json=fields)


@allure.step("List payroll runs")
def list_payroll_runs(client: ApiClient, **query):
    return client.get("/api/payroll/runs", params=query)


@allure.step("Get payroll run {run_id}")
def get_payroll_run(client: ApiClient, run_id: str):
    return client.get(f"/api/payroll/runs/{run_id}")


@allure.step("Prepare payroll run {run_id}")
def prepare_run(client: ApiClient, run_id: str, rebuild: bool = False):
    return client.post(f"/api/payroll/runs/{run_id}/prepare", params={"rebuild": str(rebuild).lower()})


@allure.step("Get payroll run readiness {run_id}")
def get_run_readiness(client: ApiClient, run_id: str):
    return client.get(f"/api/payroll/runs/{run_id}/readiness")


@allure.step("Get payroll run employees {run_id}")
def get_run_employees(client: ApiClient, run_id: str, **query):
    return client.get(f"/api/payroll/runs/{run_id}/employees", params=query)


@allure.step("Calculate payroll run {run_id}")
def calculate_run(client: ApiClient, run_id: str):
    return client.post(f"/api/payroll/runs/{run_id}/calculate")


@allure.step("Recalculate payroll run {run_id}")
def recalculate_run(client: ApiClient, run_id: str):
    return client.post(f"/api/payroll/runs/{run_id}/recalculate")


@allure.step("Get payroll run results {run_id}")
def get_run_results(client: ApiClient, run_id: str, **query):
    return client.get(f"/api/payroll/runs/{run_id}/results", params=query)


@allure.step("Get payroll run result for employee {employee_id}")
def get_run_result(client: ApiClient, run_id: str, employee_id: str):
    return client.get(f"/api/payroll/runs/{run_id}/results/{employee_id}")


@allure.step("Get payroll run calculation errors {run_id}")
def get_run_calculation_errors(client: ApiClient, run_id: str):
    return client.get(f"/api/payroll/runs/{run_id}/calculation-errors")


@allure.step("Transition payroll run {run_id} to {target}")
def transition_run(client: ApiClient, run_id: str, target: str):
    return client.post(f"/api/payroll/runs/{run_id}/transition", params={"target": target})


@allure.step("Get payroll run history {run_id}")
def get_run_history(client: ApiClient, run_id: str):
    return client.get(f"/api/payroll/runs/{run_id}/history")


# --- Statutory integration (calculation-engine visibility only; see qa/10-payroll scope note —
# configuration/compliance/filing CRUD is explicitly out of scope for this suite) ---


@allure.step("Get statutory result for run {run_id} / employee {employee_id}")
def get_run_employee_statutory(client: ApiClient, run_id: str, employee_id: str):
    return client.get(f"/api/payroll/runs/{run_id}/results/{employee_id}/statutory")


# --- Payroll Inputs ---


@allure.step("Create payroll input batch")
def create_input_batch(client: ApiClient, **fields):
    return client.post("/api/payroll/input-batches", json=fields)


@allure.step("List payroll input batches")
def list_input_batches(client: ApiClient, **query):
    return client.get("/api/payroll/input-batches", params=query)


@allure.step("Validate payroll input batch {batch_id}")
def validate_input_batch(client: ApiClient, batch_id: str):
    return client.post(f"/api/payroll/input-batches/{batch_id}/validate")


@allure.step("Get payroll input batch preview {batch_id}")
def get_input_batch_preview(client: ApiClient, batch_id: str):
    return client.get(f"/api/payroll/input-batches/{batch_id}/preview")


@allure.step("Get payroll input batch issues {batch_id}")
def get_input_batch_issues(client: ApiClient, batch_id: str, **query):
    return client.get(f"/api/payroll/input-batches/{batch_id}/issues", params=query)


@allure.step("Submit payroll input batch {batch_id}")
def submit_input_batch(client: ApiClient, batch_id: str):
    return client.post(f"/api/payroll/input-batches/{batch_id}/submit")


@allure.step("Approve payroll input batch {batch_id}")
def approve_input_batch(client: ApiClient, batch_id: str):
    return client.post(f"/api/payroll/input-batches/{batch_id}/approve")


@allure.step("Cancel payroll input batch {batch_id}")
def cancel_input_batch(client: ApiClient, batch_id: str, reason: str):
    return client.post(f"/api/payroll/input-batches/{batch_id}/cancel", json=reason)


# --- Loans (Module 11) — request/approve/disburse lifecycle + register/schedule visibility only;
# recovery's actual integration into a calculated payroll run is out of scope for this narrow batch ---


@allure.step("List loan products")
def list_loan_products(client: ApiClient):
    return client.get("/api/payroll/loan-products")


@allure.step("Create loan product")
def create_loan_product(client: ApiClient, **fields):
    return client.post("/api/payroll/loan-products", json=fields)


@allure.step("List loans")
def list_loans(client: ApiClient, **query):
    return client.get("/api/payroll/loans", params=query)


@allure.step("Get loan register")
def get_loan_register(client: ApiClient, **query):
    return client.get("/api/payroll/loans/register", params=query)


@allure.step("Create loan")
def create_loan(client: ApiClient, **fields):
    return client.post("/api/payroll/loans", json=fields)


@allure.step("Submit loan {loan_id}")
def submit_loan(client: ApiClient, loan_id: str):
    return client.post(f"/api/payroll/loans/{loan_id}/submit")


@allure.step("Approve loan {loan_id}")
def approve_loan(client: ApiClient, loan_id: str, **query):
    return client.post(f"/api/payroll/loans/{loan_id}/approve", params=query or None)


@allure.step("Record loan disbursement {loan_id}")
def disburse_loan(client: ApiClient, loan_id: str, **query):
    return client.post(f"/api/payroll/loans/{loan_id}/record-disbursement", params=query or None)


@allure.step("Get loan schedule {loan_id}")
def get_loan_schedule(client: ApiClient, loan_id: str):
    return client.get(f"/api/payroll/loans/{loan_id}/schedule")


# --- Variable Pay / Bonus (Module 12) — plan/award smoke only; settlement into an actual payroll
# run is out of scope for this narrow batch ---


@allure.step("List variable pay plans")
def list_variable_pay_plans(client: ApiClient):
    return client.get("/api/payroll/variable-pay-plans")


@allure.step("Create variable pay plan")
def create_variable_pay_plan(client: ApiClient, **fields):
    return client.post("/api/payroll/variable-pay-plans", json=fields)


@allure.step("Add variable pay plan version {plan_id}")
def add_variable_pay_plan_version(client: ApiClient, plan_id: str, **fields):
    return client.post(f"/api/payroll/variable-pay-plans/{plan_id}/versions", json=fields)


@allure.step("List variable pay awards")
def list_variable_pay_awards(client: ApiClient, **query):
    return client.get("/api/payroll/variable-pay-awards", params=query)


@allure.step("Preview variable pay award for {employee_id}")
def preview_variable_pay_award(client: ApiClient, employee_id: str, **fields):
    return client.post(
        "/api/payroll/variable-pay-awards/generate-preview", params={"employeeId": employee_id}, json=fields
    )


@allure.step("Generate variable pay award for {employee_id}")
def generate_variable_pay_award(client: ApiClient, employee_id: str, **fields):
    return client.post(
        "/api/payroll/variable-pay-awards/generate", params={"employeeId": employee_id}, json=fields
    )


@allure.step("Get variable pay award {award_id}")
def get_variable_pay_award(client: ApiClient, award_id: str):
    return client.get(f"/api/payroll/variable-pay-awards/{award_id}")


# --- Bank Advice (Module 15) ---


@allure.step("Generate bank advice for run {run_id}")
def generate_bank_advice(client: ApiClient, run_id: str):
    return client.post(f"/api/payroll/runs/{run_id}/bank-advice")


@allure.step("List bank advice batches")
def list_bank_advice(client: ApiClient, **query):
    return client.get("/api/payroll/bank-advice", params=query)


@allure.step("Get bank advice batch {batch_id}")
def get_bank_advice(client: ApiClient, batch_id: str):
    return client.get(f"/api/payroll/bank-advice/{batch_id}")


# --- GL / Accounting Integration (Module 16) ---


@allure.step("Create GL account")
def create_gl_account(client: ApiClient, **fields):
    return client.post("/api/payroll/accounting/accounts", json=fields)


@allure.step("List GL accounts")
def list_gl_accounts(client: ApiClient, **query):
    return client.get("/api/payroll/accounting/accounts", params=query)


@allure.step("Create accounting configuration")
def create_accounting_configuration(client: ApiClient, **fields):
    return client.post("/api/payroll/accounting/configurations", json=fields)


@allure.step("List accounting configurations")
def list_accounting_configurations(client: ApiClient, **query):
    return client.get("/api/payroll/accounting/configurations", params=query)


@allure.step("Create accounting configuration version {configuration_id}")
def create_accounting_configuration_version(client: ApiClient, configuration_id: str, **fields):
    return client.post(f"/api/payroll/accounting/configurations/{configuration_id}/versions", json=fields)


@allure.step("Create GL mapping for version {version_id}")
def create_gl_mapping(client: ApiClient, version_id: str, **fields):
    return client.post(f"/api/payroll/accounting/versions/{version_id}/mappings", json=fields)


@allure.step("Generate GL journal for run {run_id}")
def generate_gl_journal(client: ApiClient, run_id: str):
    return client.post(f"/api/payroll/runs/{run_id}/accounting/generate")


@allure.step("List GL journals")
def list_gl_journals(client: ApiClient, **query):
    return client.get("/api/payroll/accounting", params=query)


@allure.step("Get GL journal {journal_id}")
def get_gl_journal(client: ApiClient, journal_id: str):
    return client.get(f"/api/payroll/accounting/{journal_id}")


# --- Payroll Analytics / Reconciliation (Module 17) — read/generate smoke only ---


@allure.step("Get payroll analytics overview for run {run_id}")
def get_analytics_overview(client: ApiClient, run_id: str):
    return client.get(f"/api/payroll/analytics/runs/{run_id}/overview")


@allure.step("Get payroll control totals for run {run_id}")
def get_control_totals(client: ApiClient, run_id: str):
    return client.get(f"/api/payroll/analytics/runs/{run_id}/control-totals")


@allure.step("Get payroll run summary for run {run_id}")
def get_run_summary(client: ApiClient, run_id: str):
    return client.get(f"/api/payroll/analytics/runs/{run_id}/summary")


@allure.step("Generate {kind} reconciliation for run {run_id}")
def generate_reconciliation(client: ApiClient, run_id: str, kind: str):
    """`kind` is "pre" or "post" (PayrollReconciliationController, route api/payroll/reconciliation)."""
    return client.post(f"/api/payroll/reconciliation/runs/{run_id}/{kind}")


@allure.step("List payroll exceptions")
def list_payroll_exceptions(client: ApiClient, **query):
    return client.get("/api/payroll/exceptions", params=query)
