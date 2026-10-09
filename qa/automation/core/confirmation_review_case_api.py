"""Confirmation review-case API helpers (Module 18 / Phase 9c-2).

Every function takes an already-authenticated `ApiClient` and returns the raw `requests.Response` —
assertions stay in the tests. Named after the endpoints they hit (`ConfirmationReviewCasesController`).
This phase collects reviews only; it makes no confirm/extend/reject decision and changes no employee status.
"""

from __future__ import annotations

import allure

from core.api_client import ApiClient

_CASES = "/api/confirmation-review-cases"


@allure.step("List confirmation review cases")
def list_review_cases(client: ApiClient, **query):
    return client.get(_CASES, params=query)


@allure.step("Get confirmation review case {case_id}")
def get_review_case(client: ApiClient, case_id: str):
    return client.get(f"{_CASES}/{case_id}")


@allure.step("Create/initialize confirmation review case")
def create_review_case(client: ApiClient, **body):
    return client.post(_CASES, json=body)


@allure.step("Get reviewer form for case {case_id} ({reviewer_type})")
def get_reviewer_form(client: ApiClient, case_id: str, reviewer_type: str):
    return client.get(f"{_CASES}/{case_id}/reviewer-form", params={"reviewerType": reviewer_type})


@allure.step("Save {segment} reviewer draft for case {case_id}")
def save_reviewer_draft(client: ApiClient, case_id: str, segment: str, **body):
    return client.put(f"{_CASES}/{case_id}/reviews/{segment}/draft", json=body)


@allure.step("Submit {segment} review for case {case_id}")
def submit_reviewer_review(client: ApiClient, case_id: str, segment: str, **body):
    return client.post(f"{_CASES}/{case_id}/reviews/{segment}/submit", json=body)


@allure.step("Get confirmation review case history {case_id}")
def get_review_case_history(client: ApiClient, case_id: str):
    return client.get(f"{_CASES}/{case_id}/history")
