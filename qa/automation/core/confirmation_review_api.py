"""Confirmation review-question & rating-scale master API helpers (Module 18 / Phase 9c-1).

Every function takes an already-authenticated `ApiClient` (see `conftest.py`'s `admin_api_client` /
`employee_api_client` fixtures) and returns the raw `requests.Response` — assertions stay in the tests.
Named after the endpoints they hit (`ConfirmationRatingScalesController`, `ConfirmationReviewQuestionsController`).
"""

from __future__ import annotations

import allure

from core.api_client import ApiClient

_SCALES = "/api/confirmation-rating-scales"
_QUESTIONS = "/api/confirmation-review-questions"


# --- Rating scales ---

@allure.step("List confirmation rating scales")
def list_rating_scales(client: ApiClient, **query):
    return client.get(_SCALES, params=query)


@allure.step("Get confirmation rating scale {scale_id}")
def get_rating_scale(client: ApiClient, scale_id: str):
    return client.get(f"{_SCALES}/{scale_id}")


@allure.step("Create confirmation rating scale")
def create_rating_scale(client: ApiClient, **body):
    return client.post(_SCALES, json=body)


@allure.step("Update confirmation rating scale {scale_id}")
def update_rating_scale(client: ApiClient, scale_id: str, **body):
    return client.put(f"{_SCALES}/{scale_id}", json=body)


# --- Review questions ---

@allure.step("List confirmation review questions")
def list_review_questions(client: ApiClient, **query):
    return client.get(_QUESTIONS, params=query)


@allure.step("Get confirmation review question {question_id}")
def get_review_question(client: ApiClient, question_id: str):
    return client.get(f"{_QUESTIONS}/{question_id}")


@allure.step("Create confirmation review question")
def create_review_question(client: ApiClient, **body):
    return client.post(_QUESTIONS, json=body)


@allure.step("Update confirmation review question {question_id}")
def update_review_question(client: ApiClient, question_id: str, **body):
    return client.put(f"{_QUESTIONS}/{question_id}", json=body)
