"""Unit tests for the CI tooling itself: outcome policy, redaction, pacing math, suite partition.

No stack, browser or credentials needed. Run from qa/automation:
    python -m pytest ci -o addopts="" -q
"""

from __future__ import annotations

import json
import zipfile

import pytest

from ci import run_ci
from utils import auth_rate_guard


def result(outcome: str, message: str = "", test_id: str = "tests.api.test_x::test_y") -> dict:
    return {"id": test_id, "outcome": outcome, "message": message}


# --- Blocking outcome policy ---


def test_blocking_suite_passes_with_passes_and_ordinary_skips():
    results = [result("passed"), result("skipped", "No submittable Leave Type exists")]
    assert run_ci.classify_blocking(results, 0, []) == []


def test_blocking_suite_fails_on_any_failure_or_error():
    assert run_ci.classify_blocking([result("passed"), result("failed", "assert 500 == 200")], 1, [])
    assert run_ci.classify_blocking([result("passed"), result("error", "fixture setup failed")], 1, [])


def test_blocking_suite_fails_when_the_rate_limiter_turned_tests_into_skips():
    skipped = result("skipped", "Could not sign in as the configured QA_A_ADMIN user (429) — check ...")
    problems = run_ci.classify_blocking([result("passed"), skipped], 0, [])
    assert any("429" in p for p in problems)


def test_blocking_suite_fails_on_a_throttled_request_even_without_a_visible_skip():
    throttled = [{"test": "t", "method": "POST", "path": "/api/auth/refresh"}]
    assert run_ci.classify_blocking([result("passed")], 0, throttled)


def test_blocking_suite_fails_when_every_test_skipped():
    problems = run_ci.classify_blocking([result("skipped", "QA_TENANT_A_HOST is not set")], 0, [])
    assert any("nothing was verified" in p for p in problems)


@pytest.mark.parametrize("exit_code", [2, 3, 4, 5])
def test_blocking_suite_fails_on_abnormal_pytest_status(exit_code):
    assert run_ci.classify_blocking([result("passed")], exit_code, [])


def test_known_defect_groups_separate_expected_failures_from_fixed_defects():
    groups = run_ci.classify_known_defects(
        [result("failed", "KNOWN DEFECT F1"), result("passed"), result("error"), result("skipped")]
    )
    assert [len(groups[k]) for k in ("still_failing", "now_passing", "errored", "skipped")] == [1, 1, 1, 1]


def test_parse_junit_reads_outcomes_and_messages(tmp_path):
    junit = tmp_path / "junit.xml"
    junit.write_text(
        """<testsuites><testsuite>
        <testcase classname="tests.api.test_a" name="test_ok"/>
        <testcase classname="tests.api.test_a" name="test_bad"><failure message="assert 1 == 2">trace</failure></testcase>
        <testcase classname="tests.api.test_a" name="test_skip"><skipped type="pytest.skip" message="reason (429)"/></testcase>
        <testcase classname="tests.api.test_a" name="test_err"><error message="setup">trace</error></testcase>
        </testsuite></testsuites>""",
        encoding="utf-8",
    )
    outcomes = {r["id"].split("::")[1]: r["outcome"] for r in run_ci.parse_junit(junit)}
    assert outcomes == {"test_ok": "passed", "test_bad": "failed", "test_skip": "skipped", "test_err": "error"}


# --- Partition ---


def test_partition_flags_overlap_gaps_and_empty_suites():
    all_ids = {"a", "b", "c"}
    assert run_ci.partition_problems(all_ids, {"x": {"a"}, "y": {"b"}, "z": {"c"}}) == []
    problems = run_ci.partition_problems(all_ids, {"x": {"a", "b"}, "y": {"b"}, "z": set()})
    assert any("both select" in p for p in problems)
    assert any("belong to no suite" in p for p in problems)
    assert any("selects no tests" in p for p in problems)


# --- Redaction ---

PASSWORD = 'S3cret"Pa\\ss'
JWT = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJxYWF1dG8ifQ.c2lnbmF0dXJlLXZhbHVl"


@pytest.fixture
def secrets(monkeypatch):
    monkeypatch.setenv("QA_A_ADMIN_PASSWORD", PASSWORD)
    return run_ci.secret_values()


def test_redact_masks_passwords_in_raw_and_escaped_forms(secrets):
    once = json.dumps(PASSWORD)[1:-1]
    twice = json.dumps(once)[1:-1]
    text = f"typed {PASSWORD}; json {once}; nested {twice}"
    assert PASSWORD not in run_ci.redact(text, secrets)
    assert once not in run_ci.redact(text, secrets)
    assert twice not in run_ci.redact(text, secrets)


def test_redact_masks_tokens_and_bearer_headers(secrets):
    text = (
        f'{{"accessToken":"{JWT}","refreshToken":"opaque-refresh-123"}} '
        '{\\"refreshToken\\":\\"nested-refresh-456\\"} '
        "Authorization: Bearer abc.def-ghi"
    )
    cleaned = run_ci.redact(text, secrets)
    for leaked in (JWT, "opaque-refresh-123", "nested-refresh-456", "abc.def-ghi"):
        assert leaked not in cleaned


def test_scrub_rewrites_trace_zips_and_leaves_images_alone(tmp_path, secrets):
    trace = tmp_path / "trace.zip"
    png = b"\x89PNG\r\n" + JWT.encode()
    with zipfile.ZipFile(trace, "w") as archive:
        archive.writestr("trace.trace", json.dumps({"method": "fill", "params": {"value": PASSWORD}}))
        archive.writestr("trace.network", json.dumps({"postData": json.dumps({"password": PASSWORD})}))
        archive.writestr("resources/shot.png", png)
    report = tmp_path / "junit.xml"
    report.write_text(f"<failure message='{JWT}'/>", encoding="utf-8")

    run_ci.scrub([tmp_path])

    with zipfile.ZipFile(trace) as archive:
        assert PASSWORD not in archive.read("trace.trace").decode()
        assert "S3cret" not in archive.read("trace.network").decode()
        assert archive.read("resources/shot.png") == png
    assert JWT not in report.read_text(encoding="utf-8")


def test_short_values_are_not_treated_as_secrets(monkeypatch):
    monkeypatch.setenv("QA_A_EMPLOYEE_PASSWORD", "abc")
    assert "abc" not in run_ci.secret_values()


# --- Auth rate pacing ---


@pytest.fixture
def clean_guard(monkeypatch):
    auth_rate_guard._events.clear()
    yield auth_rate_guard
    auth_rate_guard._events.clear()


def test_only_limited_endpoints_are_recorded(clean_guard):
    clean_guard.record("/api/auth/login", "POST", now=0)
    clean_guard.record("http://anevra01.localhost:5080/api/tenants/current/branding?x=1", "GET", now=1)
    clean_guard.record("/api/auth/login", "OPTIONS", now=2)
    clean_guard.record("/api/employees", "GET", now=3)
    assert len(clean_guard._events) == 2


def test_wait_is_zero_while_the_window_has_room(clean_guard):
    for t in range(5):
        clean_guard.record("/api/auth/login", "POST", now=t)
    assert clean_guard.seconds_to_wait(threshold=10, now=5, window=62) == 0


def test_wait_lasts_until_enough_old_requests_leave_the_window(clean_guard):
    for t in range(15):
        clean_guard.record("/api/auth/login", "POST", now=float(t))
    # 15 recorded, threshold 10: the 5th-oldest (t=4) must expire, at 4 + 62.
    assert clean_guard.seconds_to_wait(threshold=10, now=20.0, window=62) == pytest.approx(4 + 62 - 20 + 0.25)


def test_expired_requests_are_forgotten(clean_guard):
    for t in range(15):
        clean_guard.record("/api/auth/login", "POST", now=float(t))
    assert clean_guard.seconds_to_wait(threshold=0, now=100.0, window=62) == 0
    assert not clean_guard._events
