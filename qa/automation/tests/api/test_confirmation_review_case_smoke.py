"""API smoke/lifecycle suite for Module 18 — Confirmation review cases (Phase 9c-2).

Automated from qa/18-confirmation-probation/cases/05-review-cases.yaml. Covers the review-case lifecycle
(initialize → snapshot questions → reviewer draft → submit → history), idempotent creation, and the
authorization boundary (unauthenticated 401, Employee-without-manage 403).

Requires QA_TENANT_A_HOST plus QA_A_ADMIN_USERNAME/PASSWORD (admin_api_client — TenantAdmin, holds every
Confirmation.* permission) and QA_A_EMPLOYEE_USERNAME/PASSWORD (employee_api_client — holds none). The
lifecycle test picks the first employee in the tenant; if no published confirmation policy resolves for that
employee it honest-skips (9c-2 requires a resolvable 9a policy). It never changes employee status — 9c-2
collects reviews only.
"""

from __future__ import annotations

import allure
import pytest

from core.confirmation_review_case_api import (
    create_review_case,
    get_review_case_history,
    get_reviewer_form,
    list_review_cases,
    save_reviewer_draft,
    submit_reviewer_review,
)
from core.employee_api import list_employees
from utils.env_utils import require_env

pytestmark = [allure.feature("Confirmation & Probation"), pytest.mark.api]


def _answer_for(question: dict) -> dict:
    """A valid answer for a snapshot question, by type (validated against the snapshot server-side)."""
    qid = question["id"]
    qtype = question["questionType"]
    if qtype == "Rating":
        rating = question["ratingValues"][0]["ratingValue"] if question["ratingValues"] else question.get("ratingMin", 1)
        return {"confirmationReviewCaseQuestionId": qid, "ratingValue": rating}
    if qtype == "YesNo":
        return {"confirmationReviewCaseQuestionId": qid, "boolValue": True}
    if qtype == "Text":
        return {"confirmationReviewCaseQuestionId": qid, "textValue": "QA automated response"}
    if qtype in ("Dropdown", "MultiSelect"):
        codes = [question["options"][0]["code"]] if question["options"] else []
        return {"confirmationReviewCaseQuestionId": qid, "selectedOptionCodes": codes}
    return {"confirmationReviewCaseQuestionId": qid}


# --- Read + authorization boundary (no seed data needed) ---

@allure.story("Review cases")
@allure.title("CONF-RC-001 — GET /api/confirmation-review-cases returns a paged list for an authorized admin")
@pytest.mark.smoke
@pytest.mark.regression
def test_review_cases_list_is_readable(admin_api_client):
    response = list_review_cases(admin_api_client, page=1, pageSize=5)
    assert response.status_code == 200, response.text
    body = response.json()["data"]
    assert "items" in body and "totalCount" in body


@allure.story("Authorization")
@allure.title("CONF-RC-401 — no bearer token is rejected 401 on the review-case surface")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_unauthenticated_call_is_rejected(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    anonymous = api_client_factory(host)
    assert list_review_cases(anonymous).status_code == 401


@allure.story("Authorization")
@allure.title("CONF-RC-403 — an Employee without ReviewCaseManage cannot initialize a case")
@pytest.mark.regression
def test_employee_cannot_initialize_case(employee_api_client):
    response = create_review_case(employee_api_client, employeeId="00000000-0000-0000-0000-000000000000")
    assert response.status_code == 403, response.text


# --- Full lifecycle (requires a resolvable 9a policy for an employee) ---

@allure.story("Review cases")
@allure.title("CONF-RC-010 — initialize → snapshot → draft → submit → history; creation is idempotent")
@pytest.mark.regression
def test_review_case_lifecycle(admin_api_client):
    employees = list_employees(admin_api_client, page=1, pageSize=1)
    assert employees.status_code == 200, employees.text
    items = employees.json()["data"]["items"]
    if not items:
        pytest.skip("No employees exist in this tenant to open a review case for.")
    employee_id = items[0]["id"]

    created = create_review_case(admin_api_client, employeeId=employee_id)
    if created.status_code != 201:
        pytest.skip(
            "Review case creation requires a resolvable published confirmation policy for the employee "
            f"(got {created.status_code}: {created.text[:200]})."
        )
    detail = created.json()["data"]
    case_id = detail["case"]["id"]

    # Idempotent: a second call returns the same case.
    again = create_review_case(admin_api_client, employeeId=employee_id)
    assert again.status_code in (200, 201), again.text
    assert again.json()["data"]["case"]["id"] == case_id

    form = get_reviewer_form(admin_api_client, case_id, "ReportingManager")
    assert form.status_code == 200, form.text
    questions = form.json()["data"]["questions"]

    # Draft saves without requiring mandatory answers.
    draft = save_reviewer_draft(admin_api_client, case_id, "manager", overallComments="QA draft")
    assert draft.status_code == 200, draft.text
    assert draft.json()["data"]["status"] == "Draft"

    # Submit must answer every snapshot question (mandatory enforced server-side).
    responses = [_answer_for(q) for q in questions]
    submitted = submit_reviewer_review(admin_api_client, case_id, "manager", overallComments="QA submit", responses=responses)
    assert submitted.status_code == 200, submitted.text
    assert submitted.json()["data"]["status"] == "Submitted"

    history = get_review_case_history(admin_api_client, case_id)
    assert history.status_code == 200, history.text
    event_types = {event["eventType"] for event in history.json()["data"]}
    assert "CaseCreated" in event_types
    assert "ReviewSubmitted" in event_types
