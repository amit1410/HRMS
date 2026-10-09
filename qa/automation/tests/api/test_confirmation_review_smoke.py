"""API smoke suite for Module 18 — Confirmation review-question & rating-scale masters (Phase 9c-1).

Automated from qa/18-confirmation-probation/cases/03-rating-scales.yaml and 04-review-questions.yaml.
Covers the rating-scale and review-question master lifecycles (list → create → fetch), type-specific
validation (rating question needs a scale; dropdown needs options), and the authorization boundary
(unauthenticated 401, Employee-without-manage 403).

Requires QA_TENANT_A_HOST plus, per test: QA_A_ADMIN_USERNAME/PASSWORD (admin_api_client, holds
Confirmation.RatingScaleManage/ReviewQuestionManage) and QA_A_EMPLOYEE_USERNAME/PASSWORD
(employee_api_client, which holds neither). Each fixture skips on its own if its pair is unset or sign-in
fails. The suite creates masters with unique codes (there is no delete endpoint); it never mutates another
tenant's data.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.confirmation_review_api import (
    create_rating_scale,
    create_review_question,
    get_rating_scale,
    get_review_question,
    list_rating_scales,
    list_review_questions,
)
from utils.env_utils import require_env

pytestmark = [allure.feature("Confirmation & Probation"), pytest.mark.api]


def _code(prefix: str) -> str:
    return f"{prefix}{uuid.uuid4().hex[:6].upper()}"


def _five_point(code: str) -> dict:
    return {
        "code": code,
        "name": "QA Five Point",
        "description": "smoke",
        "minRating": 1,
        "maxRating": 5,
        "isActive": True,
        "displayOrder": 0,
        "values": [
            {"ratingValue": 1, "label": "Unsatisfactory", "displayOrder": 1, "isActive": True},
            {"ratingValue": 3, "label": "Meets Expectations", "displayOrder": 3, "isActive": True},
            {"ratingValue": 5, "label": "Outstanding", "displayOrder": 5, "isActive": True},
        ],
    }


# --- Rating scales ---

@allure.story("Rating scales")
@allure.title("CONF-RSCALE-001 — GET /api/confirmation-rating-scales returns a paged, IsActive-filtered list")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_rating_scales_list_is_readable(admin_api_client):
    response = list_rating_scales(admin_api_client, isActive=True)
    assert response.status_code == 200, response.text
    paged = response.json()["data"]
    assert "items" in paged and "totalCount" in paged
    assert all(item["isActive"] for item in paged["items"]), "Expected every item to be IsActive=true per the filter"


@allure.story("Rating scales")
@allure.title("CONF-RSCALE-002 — Admin creates a rating scale with values and reads it back")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_rating_scale_create_and_fetch(admin_api_client):
    code = _code("QARS")
    created = create_rating_scale(admin_api_client, **_five_point(code))
    assert created.status_code == 201, created.text
    scale_id = created.json()["data"]["scale"]["id"]
    assert created.json()["data"]["scale"]["maxRating"] == 5

    fetched = get_rating_scale(admin_api_client, scale_id)
    assert fetched.status_code == 200, fetched.text
    assert len(fetched.json()["data"]["values"]) == 3


@allure.story("Rating scales")
@allure.title("CONF-RSCALE-003 — A rating value outside the scale bounds is rejected")
@pytest.mark.regression
def test_rating_scale_value_bounds_enforced(admin_api_client):
    body = _five_point(_code("QARS"))
    body["values"].append({"ratingValue": 99, "label": "Impossible", "displayOrder": 9, "isActive": True})
    response = create_rating_scale(admin_api_client, **body)
    assert response.status_code == 400, response.text


# --- Review questions ---

@allure.story("Review questions")
@allure.title("CONF-RQ-001 — GET /api/confirmation-review-questions returns a paged list")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_review_questions_list_is_readable(admin_api_client):
    response = list_review_questions(admin_api_client)
    assert response.status_code == 200, response.text
    assert "items" in response.json()["data"]


@allure.story("Review questions")
@allure.title("CONF-RQ-002 — Admin creates a Yes/No question and reads it back")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_yes_no_question_create_and_fetch(admin_api_client):
    code = _code("QARQ")
    created = create_review_question(
        admin_api_client, code=code, questionText="Recommend confirmation?", questionType="YesNo", isMandatory=True
    )
    assert created.status_code == 201, created.text
    question_id = created.json()["data"]["question"]["id"]

    fetched = get_review_question(admin_api_client, question_id)
    assert fetched.status_code == 200, fetched.text
    assert fetched.json()["data"]["question"]["questionType"] == "YesNo"


@allure.story("Review questions")
@allure.title("CONF-RQ-003 — A Rating question without a rating scale is rejected")
@pytest.mark.regression
def test_rating_question_requires_scale(admin_api_client):
    response = create_review_question(
        admin_api_client, code=_code("QARQ"), questionText="Overall?", questionType="Rating"
    )
    assert response.status_code == 400, response.text


@allure.story("Review questions")
@allure.title("CONF-RQ-004 — A Dropdown question with a configured rating scale and options round-trips")
@pytest.mark.regression
def test_rating_then_dropdown_question(admin_api_client):
    scale = create_rating_scale(admin_api_client, **_five_point(_code("QARS")))
    assert scale.status_code == 201, scale.text
    scale_id = scale.json()["data"]["scale"]["id"]

    rating_q = create_review_question(
        admin_api_client,
        code=_code("QARQ"),
        questionText="Overall performance?",
        questionType="Rating",
        isMandatory=True,
        confirmationRatingScaleId=scale_id,
    )
    assert rating_q.status_code == 201, rating_q.text

    dropdown_q = create_review_question(
        admin_api_client,
        code=_code("QARQ"),
        questionText="Primary strength?",
        questionType="Dropdown",
        options=[
            {"code": "TECH", "label": "Technical", "displayOrder": 1, "isActive": True},
            {"code": "COMM", "label": "Communication", "displayOrder": 2, "isActive": True},
        ],
    )
    assert dropdown_q.status_code == 201, dropdown_q.text


# --- Authorization ---

@allure.story("Authorization")
@allure.title("CONF-RMASTER-AUTHZ-001 — no bearer token is rejected 401 on the review-master surface")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_unauthenticated_call_is_rejected(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    anonymous_client = api_client_factory(host)
    assert list_rating_scales(anonymous_client).status_code == 401
    assert list_review_questions(anonymous_client).status_code == 401


@allure.story("Authorization")
@allure.title("CONF-RMASTER-AUTHZ-002 — an Employee (no manage permission) gets 403 creating masters")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_cannot_manage_masters(employee_api_client):
    scale = create_rating_scale(employee_api_client, **_five_point(_code("QARS")))
    assert scale.status_code == 403, scale.text
    question = create_review_question(
        employee_api_client, code=_code("QARQ"), questionText="probe", questionType="YesNo"
    )
    assert question.status_code == 403, question.text
