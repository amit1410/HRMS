"""CI entry point for the HRMS QA automation suite (.github/workflows/qa-automation.yml).

Every CI step calls this script with one plain command, so the same steps work in bash, pwsh and
Windows PowerShell, and a CI run can be reproduced locally from qa/automation:

    python ci/run_ci.py collect                    verify collection and the suite partition (no stack needed)
    python ci/run_ci.py preflight --suite smoke    required variables present, API and UI reachable
    python ci/run_ci.py run --suite smoke          run one suite, classify the outcome, write a summary
    python ci/run_ci.py scrub                      redact secrets and tokens from reports before upload

Suites partition the collected tests; `collect` fails if they overlap or miss a test:
    smoke          not known_defect and not e2e   blocking
    e2e            e2e and not known_defect       blocking; writes only to the QAAUTO payroll sandbox
    known-defects  known_defect                   report only; never fails the run

A blocking suite fails on any test failure or error, on any 429 from the auth rate limiter (those
tests skipped instead of running), on a run where every selected test skipped, and on any pytest
exit status other than 0 or 1. Every other skip is reported, not failed.

Nothing here prints a variable's value. A local .env is loaded the same way conftest.py loads it. Outputs go to reports/ci/<suite>/ and test-results/<suite>/.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shlex
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from dotenv import load_dotenv  # noqa: E402

# A local .env, like conftest.py and the sandbox tool load. CI checkouts have none.
load_dotenv(ROOT / ".env")

SUITES = {
    "smoke": {"marker": "not known_defect and not e2e", "blocking": True},
    "e2e": {"marker": "e2e and not known_defect", "blocking": True},
    "known-defects": {"marker": "known_defect", "blocking": False},
}

# Defaults `run` applies when the caller hasn't set them, so a local run paces and reuses sessions
# exactly like CI. 18 leaves a margin of 2 under the server's 20 per 60s.
CI_DEFAULTS = {
    "QA_AUTH_RATE_BUDGET": "18",
    "QA_REUSE_API_SESSIONS": "1",
    "QA_HEADLESS": "true",
}

ADMIN_USERNAME_NAMES = ("QA_A_ADMIN_USERNAME", "QA_A_ADMIN_IDENTIFIER")
REQUIRED_ENV = {
    "smoke": ["QA_TENANT_A_HOST", "QA_A_ADMIN_PASSWORD", "QA_A_EMPLOYEE_USERNAME", "QA_A_EMPLOYEE_PASSWORD",
              "QA_A_MANAGER_USERNAME", "QA_A_MANAGER_PASSWORD"],
    "e2e": ["QA_TENANT_A_HOST", "QA_A_ADMIN_PASSWORD", "QA_PAYROLL_SANDBOX", "QA_PAYROLL_SANDBOX_TENANT",
            "QA_SANDBOX_STATE_DIR"],
    "known-defects": ["QA_TENANT_A_HOST", "QA_A_ADMIN_PASSWORD"],
}

MASK = "***redacted***"
JWT_RE = re.compile(r"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}")
TOKEN_FIELD_RE = re.compile(r'(\\*"(?:accessToken|refreshToken|password|newPassword|currentPassword)\\*"\s*:\s*\\*")([^"\\]+)')
BEARER_RE = re.compile(r"(Bearer\s+)[A-Za-z0-9\-._~+/]+=*")
BINARY_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp", ".gif", ".webm", ".mp4"}
RATE_LIMIT_MESSAGE_RE = re.compile(r"\(429\)|\b429 Too Many Requests\b")


def out_dir(suite: str) -> Path:
    return ROOT / "reports" / "ci" / suite


def in_github_actions() -> bool:
    return os.environ.get("GITHUB_ACTIONS") == "true"


def annotate(level: str, title: str, message: str) -> None:
    if in_github_actions():
        escaped = message.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::{level} title={title}::{escaped}", flush=True)
    else:
        print(f"[{level}] {title}: {message}", flush=True)


def write_summary(suite: str, markdown: str) -> None:
    markdown = redact(markdown, secret_values())
    target = out_dir(suite)
    target.mkdir(parents=True, exist_ok=True)
    (target / "summary.md").write_text(markdown, encoding="utf-8")
    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        with open(step_summary, "a", encoding="utf-8") as handle:
            handle.write(markdown + "\n")
    print(markdown, flush=True)


# --- Redaction -----------------------------------------------------------------------------------


def secret_values() -> list[str]:
    """Every configured QA password, in the raw and JSON-escaped forms a report or trace can hold it in."""
    values: set[str] = set()
    for name, value in os.environ.items():
        if name.startswith("QA_") and name.endswith("PASSWORD") and value and len(value) >= 4:
            once = json.dumps(value)[1:-1]
            twice = json.dumps(once)[1:-1]
            values.update({value, once, twice})
    return sorted(values, key=len, reverse=True)


def redact(text: str, secrets: list[str]) -> str:
    for secret in secrets:
        text = text.replace(secret, MASK)
    text = JWT_RE.sub(MASK, text)
    text = TOKEN_FIELD_RE.sub(lambda m: m.group(1) + MASK, text)
    return BEARER_RE.sub(lambda m: m.group(1) + MASK, text)


def redact_bytes(name: str, data: bytes, secrets: list[str]) -> bytes:
    if Path(name).suffix.lower() in BINARY_SUFFIXES:
        return data
    try:
        return redact(data.decode("utf-8"), secrets).encode("utf-8")
    except UnicodeDecodeError:
        for secret in secrets:
            data = data.replace(secret.encode("utf-8"), MASK.encode("utf-8"))
        return data


def scrub_zip(path: Path, secrets: list[str]) -> None:
    """Rewrites a Playwright trace (or any zip) with every entry redacted. Traces record typed
    values, request bodies and responses, so an unscrubbed trace holds the sign-in password."""
    with zipfile.ZipFile(path) as source:
        entries = [(info, source.read(info)) for info in source.infolist()]
    handle, temp_name = tempfile.mkstemp(suffix=".zip", dir=path.parent)
    os.close(handle)
    with zipfile.ZipFile(temp_name, "w", compression=zipfile.ZIP_DEFLATED) as target:
        for info, data in entries:
            target.writestr(info, redact_bytes(info.filename, data, secrets))
    os.replace(temp_name, path)


def scrub(paths: list[Path]) -> int:
    secrets = secret_values()
    count = 0
    for base in paths:
        if not base.exists():
            continue
        files = [base] if base.is_file() else [p for p in base.rglob("*") if p.is_file()]
        for file in files:
            if file.suffix.lower() == ".zip":
                scrub_zip(file, secrets)
            elif file.suffix.lower() not in BINARY_SUFFIXES:
                original = file.read_bytes()
                cleaned = redact_bytes(file.name, original, secrets)
                if cleaned != original:
                    file.write_bytes(cleaned)
            count += 1
    return count


# --- JUnit parsing and outcome policy --------------------------------------------------------------


def parse_junit(path: Path) -> list[dict]:
    if not path.exists():
        return []
    results = []
    for case in ET.parse(path).getroot().iter("testcase"):
        test_id = f"{case.get('classname', '')}::{case.get('name', '')}"
        outcome, message = "passed", ""
        for tag in ("failure", "error", "skipped"):
            element = case.find(tag)
            if element is not None:
                outcome = {"failure": "failed", "error": "error", "skipped": "skipped"}[tag]
                message = element.get("message") or (element.text or "")
                break
        results.append({"id": test_id, "outcome": outcome, "message": message.strip()})
    return results


def first_line(text: str, limit: int = 200) -> str:
    line = text.strip().splitlines()[0] if text.strip() else ""
    return line if len(line) <= limit else line[: limit - 1] + "…"


def classify_blocking(results: list[dict], exit_code: int, throttled: list[dict]) -> list[str]:
    """Reasons the suite fails; empty means it passes."""
    problems = []
    counts = Counter(r["outcome"] for r in results)
    if exit_code not in (0, 1):
        problems.append(f"pytest exited with status {exit_code} (interrupted, internal error, usage error or nothing collected).")
    if counts["failed"] or counts["error"]:
        problems.append(f"{counts['failed']} failed and {counts['error']} errored test(s). None is a known defect.")
    rate_limited = [r for r in results if r["outcome"] != "passed" and RATE_LIMIT_MESSAGE_RE.search(r["message"])]
    if throttled or rate_limited:
        problems.append(
            f"The auth rate limiter returned 429 ({len(throttled)} request(s), {len(rate_limited)} test(s) affected). "
            "Those tests did not really run. Raise QA_AUTH_RATE_RESERVE_* or lower QA_AUTH_RATE_BUDGET; never the server limit."
        )
    executed = counts["passed"] + counts["failed"] + counts["error"]
    if results and executed == 0:
        problems.append("Every selected test skipped, so nothing was verified. Check credentials and the environment.")
    if not results and exit_code in (0, 1):
        problems.append("No JUnit results were produced.")
    return problems


def classify_known_defects(results: list[dict]) -> dict:
    return {
        "still_failing": [r for r in results if r["outcome"] == "failed"],
        "now_passing": [r for r in results if r["outcome"] == "passed"],
        "errored": [r for r in results if r["outcome"] == "error"],
        "skipped": [r for r in results if r["outcome"] == "skipped"],
    }


def load_ledger(path: Path) -> dict:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def pacing_line(ledger: dict) -> str:
    if not ledger:
        return "Auth rate pacing: no ledger written."
    over = [t for t in ledger.get("tests", [])
            if t["limitedRequests"] > (ledger["reserveUi"] if t["ui"] else ledger["reserveApi"])]
    budget = ledger.get("budget")
    mode = f"budget {budget} per {ledger.get('windowSeconds')}s" if budget else "pacing off"
    return (
        f"Auth rate pacing ({mode}): {ledger.get('limitedRequests', 0)} limited request(s), "
        f"waited {ledger.get('totalWaitSeconds', 0)}s, {len(ledger.get('throttled', []))} throttled (429), "
        f"{len(over)} test(s) above their reserve."
    )


def counts_table(results: list[dict]) -> str:
    counts = Counter(r["outcome"] for r in results)
    return (
        "| passed | failed | errors | skipped | total |\n|---|---|---|---|---|\n"
        f"| {counts['passed']} | {counts['failed']} | {counts['error']} | {counts['skipped']} | {len(results)} |"
    )


def skip_reasons(results: list[dict]) -> str:
    reasons = Counter(first_line(r["message"], 160) for r in results if r["outcome"] == "skipped")
    return "\n".join(f"- {n} × {reason}" for reason, n in reasons.most_common()) or "- none"


def bullet(rows: list[dict]) -> str:
    return "\n".join(f"- `{r['id']}` — {first_line(r['message'])}" for r in rows) or "- none"


# --- Commands ------------------------------------------------------------------------------------


def pytest_base(suite: str) -> list[str]:
    return [sys.executable, "-m", "pytest", "tests", "-m", SUITES[suite]["marker"], "-p", "no:cacheprovider"]


def collected_ids(marker: str | None) -> tuple[int, set[str], str]:
    target = out_dir("collect")
    cmd = [sys.executable, "-m", "pytest", "tests", "--collect-only", "-q", "-p", "no:cacheprovider",
           f"--alluredir={target / 'allure-results'}"]
    if marker:
        cmd += ["-m", marker]
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
                          env=child_env())
    ids = {line.strip() for line in proc.stdout.splitlines() if "::" in line and not line.startswith(" ")}
    return proc.returncode, ids, proc.stdout + proc.stderr


def partition_problems(all_ids: set[str], by_suite: dict[str, set[str]]) -> list[str]:
    problems = []
    names = list(by_suite)
    for i, a in enumerate(names):
        if not by_suite[a]:
            problems.append(f"Suite '{a}' selects no tests.")
        for b in names[i + 1:]:
            overlap = by_suite[a] & by_suite[b]
            if overlap:
                problems.append(f"Suites '{a}' and '{b}' both select {len(overlap)} test(s), e.g. {sorted(overlap)[0]}.")
    missed = all_ids - set().union(*by_suite.values())
    if missed:
        problems.append(f"{len(missed)} collected test(s) belong to no suite, e.g. {sorted(missed)[0]}.")
    return problems


def cmd_collect(_: argparse.Namespace) -> int:
    code, all_ids, output = collected_ids(None)
    problems = [] if code == 0 else [f"Collecting every test failed (pytest status {code}):\n```\n{output[-3000:]}\n```"]
    by_suite = {}
    for suite, spec in SUITES.items():
        suite_code, ids, suite_output = collected_ids(spec["marker"])
        if suite_code not in (0, 5):
            problems.append(f"Collecting suite '{suite}' failed (pytest status {suite_code}):\n```\n{suite_output[-2000:]}\n```")
        by_suite[suite] = ids
    problems += partition_problems(all_ids, by_suite)

    modules = Counter(i.split("::", 1)[0] for i in all_ids)
    lines = [
        "## QA automation — collection check",
        f"Result: **{'FAIL' if problems else 'PASS'}**",
        "",
        "| suite | marker expression | blocking | tests |",
        "|---|---|---|---|",
        *(f"| {s} | `{spec['marker']}` | {'yes' if spec['blocking'] else 'no'} | {len(by_suite[s])} |" for s, spec in SUITES.items()),
        f"| all | (none) | | {len(all_ids)} |",
        "",
        "Tests per module:",
        *(f"- `{m}`: {n}" for m, n in sorted(modules.items())),
    ]
    if problems:
        lines += ["", "Problems:", *(f"- {p}" for p in problems)]
        for p in problems:
            annotate("error", "Collection check", first_line(p))
    write_summary("collect", "\n".join(lines))
    return 1 if problems else 0


def cmd_preflight(args: argparse.Namespace) -> int:
    import requests

    from config.settings import load_settings

    suite = args.suite
    problems, warnings = [], []
    if not any(os.environ.get(n) for n in ADMIN_USERNAME_NAMES):
        problems.append("QA_A_ADMIN_USERNAME is not set.")
    problems += [f"{name} is not set." for name in REQUIRED_ENV[suite] if not os.environ.get(name)]

    if suite == "e2e" and os.environ.get("QA_PAYROLL_SANDBOX", "1") != "1":
        problems.append("QA_PAYROLL_SANDBOX must be 1 for the E2E suite.")
    if suite in ("e2e", "known-defects"):
        state_dir = os.environ.get("QA_SANDBOX_STATE_DIR")
        if not state_dir:
            if suite == "known-defects":
                warnings.append("QA_SANDBOX_STATE_DIR is not set; the GL known-defect checks that read sandbox months will skip.")
        elif not Path(state_dir).is_dir():
            problems.append("QA_SANDBOX_STATE_DIR does not point at an existing directory on the runner.")
        elif not (Path(state_dir) / "payroll-sandbox-manifest.json").exists():
            warnings.append(
                "QA_SANDBOX_STATE_DIR holds no payroll-sandbox-manifest.json. If this tenant already has sandbox months, "
                "copy the existing manifest there first; preflight G7 will otherwise refuse every write."
            )

    settings = load_settings()
    host = os.environ.get("QA_TENANT_A_HOST")
    if host:
        try:
            response = requests.get(settings.api_url("/api/employees"), headers={"Host": host}, timeout=10)
            if response.status_code != 401:
                problems.append(
                    f"API at {settings.api_url('/')} answered {response.status_code} for an unauthenticated tenant request; "
                    "expected 401. Check that the API is running and QA_TENANT_A_HOST names an active tenant."
                )
        except requests.RequestException as exc:
            problems.append(f"API at {settings.api_url('/')} is not reachable: {type(exc).__name__}.")
        if suite == "smoke":
            ui = f"{settings.scheme}://localhost:{settings.ui_port}/"
            try:
                response = requests.get(ui, headers={"Host": f"{host}:{settings.ui_port}"}, timeout=10)
                if response.status_code != 200:
                    problems.append(f"UI at {ui} answered {response.status_code} for Host {host}; expected 200.")
            except requests.RequestException as exc:
                problems.append(f"UI at {ui} is not reachable: {type(exc).__name__}.")

    for warning in warnings:
        annotate("warning", f"Preflight ({suite})", warning)
    for problem in problems:
        annotate("error", f"Preflight ({suite})", problem)
    if not problems:
        print(f"Preflight for '{suite}' passed.", flush=True)
    return 1 if problems else 0


def child_env(extra: dict | None = None) -> dict:
    env = {**os.environ, "PYTHONIOENCODING": "utf-8", "PYTHONUTF8": "1", "PYTHONUNBUFFERED": "1"}
    env.update(extra or {})
    return env


def cmd_run(args: argparse.Namespace) -> int:
    suite = args.suite
    target = out_dir(suite)
    target.mkdir(parents=True, exist_ok=True)
    ledger_path = target / "auth-rate-ledger.json"
    ledger_path.unlink(missing_ok=True)

    env = child_env({"QA_AUTH_RATE_LEDGER": str(ledger_path)})
    for name, value in CI_DEFAULTS.items():
        if not env.get(name):
            env[name] = value

    cmd = pytest_base(suite) + [
        "-v",
        f"--junitxml={target / 'junit.xml'}",
        "-o", "junit_family=xunit2",
        f"--alluredir={target / 'allure-results'}",
        "--clean-alluredir",
        f"--output={ROOT / 'test-results' / suite}",
        "--browser", "chromium",
    ] + shlex.split(os.environ.get("QA_CI_EXTRA_PYTEST_ARGS", ""))
    print("Running:", " ".join(cmd[1:]), flush=True)

    with open(target / "pytest.log", "w", encoding="utf-8") as log_file:
        proc = subprocess.Popen(cmd, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                text=True, encoding="utf-8", errors="replace")
        for line in proc.stdout:
            sys.stdout.write(line)
            log_file.write(line)
        exit_code = proc.wait()

    results = parse_junit(target / "junit.xml")
    ledger = load_ledger(ledger_path)
    throttled = ledger.get("throttled", [])

    if SUITES[suite]["blocking"]:
        problems = classify_blocking(results, exit_code, throttled)
        lines = [
            f"## QA automation — {suite} (blocking)",
            f"Result: **{'FAIL' if problems else 'PASS'}** (pytest status {exit_code})",
            "",
            counts_table(results),
            "",
            pacing_line(ledger),
            "",
            "### Unexpected failures",
            bullet([r for r in results if r["outcome"] in ("failed", "error")]),
            "",
            "### Skips by reason",
            skip_reasons(results),
        ]
        if problems:
            lines += ["", "### Why this suite failed", *(f"- {p}" for p in problems)]
            for p in problems:
                annotate("error", f"{suite} suite", p)
        write_summary(suite, "\n".join(lines))
        return 1 if problems else 0

    groups = classify_known_defects(results)
    for row in groups["now_passing"]:
        annotate("warning", "Known defect now passes",
                 f"{row['id']} passed. Confirm the defect is fixed, then remove its known_defect marker.")
    lines = [
        f"## QA automation — {suite} (non-blocking)",
        "Known-defect checks assert the correct contract, so each fails while its defect exists. "
        "This job never fails the workflow.",
        "",
        counts_table(results),
        "",
        pacing_line(ledger),
        "",
        f"### Still failing as expected ({len(groups['still_failing'])})",
        bullet(groups["still_failing"]),
        "",
        f"### Now passing: defect may be fixed ({len(groups['now_passing'])})",
        bullet(groups["now_passing"]),
        "",
        f"### Errored in setup ({len(groups['errored'])})",
        bullet(groups["errored"]),
        "",
        "### Skips by reason",
        skip_reasons(results),
    ]
    if exit_code not in (0, 1):
        lines += ["", f"pytest exited with status {exit_code}; the results above may be incomplete."]
    write_summary(suite, "\n".join(lines))
    return 0


def cmd_scrub(args: argparse.Namespace) -> int:
    paths = [Path(p) if Path(p).is_absolute() else ROOT / p for p in (args.paths or ["reports/ci", "test-results"])]
    count = scrub(paths)
    print(f"Scrubbed {count} file(s) under {', '.join(str(p) for p in paths)}.", flush=True)
    return 0


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("collect").set_defaults(handler=cmd_collect)
    for name, handler in (("preflight", cmd_preflight), ("run", cmd_run)):
        sub = commands.add_parser(name)
        sub.add_argument("--suite", choices=sorted(SUITES), required=True)
        sub.set_defaults(handler=handler)
    scrub_parser = commands.add_parser("scrub")
    scrub_parser.add_argument("paths", nargs="*")
    scrub_parser.set_defaults(handler=cmd_scrub)
    args = parser.parse_args(argv)
    return args.handler(args)


if __name__ == "__main__":
    sys.exit(main())
