"""Reusable test data: credential lookups and deliberately-invalid values.

Real credentials are never hard-coded (per CLAUDE.md and the framework brief) — they are read
from the environment via `utils.env_utils.require_env`, which skips the test with a clear reason
if the variable is missing, rather than the suite fabricating a pass or a misleading failure.

The "known invalid" constants below are not secrets: they are intentionally-wrong values used to
exercise negative paths (wrong password, unknown identifier) and never correspond to a real
account.
"""

from __future__ import annotations

import datetime
import uuid
from dataclasses import dataclass

from utils.env_utils import require_env, require_env_any

KNOWN_INVALID_IDENTIFIER = "QA-AUTOMATION-UNKNOWN-USER-0000"
KNOWN_INVALID_PASSWORD = "Not-A-Real-Password-0000!"  # noqa: S105 - intentionally wrong, not a secret

#: Prefix every record this framework creates carries, so it is unmistakably test data — never
#: mistaken for (and never colliding with) a real employee — and easy to find/sweep by hand if a
#: cleanup step is ever skipped (see README "Test data and cleanup").
TEST_DATA_MARKER = "QAAUTO"


@dataclass(frozen=True)
class TenantUser:
    identifier: str
    password: str

    # Never leak the password through a stray print/log/pytest failure repr of this object —
    # only the raw `.password` attribute access exposes it, which callers use deliberately once.
    def __repr__(self) -> str:
        return f"TenantUser(identifier={self.identifier!r}, password='***redacted***')"

    __str__ = __repr__


def admin_user_a() -> TenantUser:
    """A disposable QA user in Tenant A.

    Reads QA_A_ADMIN_USERNAME / QA_A_ADMIN_PASSWORD (falling back to the Phase 1 name
    QA_A_ADMIN_IDENTIFIER for the username, if that's what's already set). Skips — never
    fabricates — when the required variables aren't configured.
    """
    return TenantUser(
        identifier=require_env_any("QA_A_ADMIN_USERNAME", "QA_A_ADMIN_IDENTIFIER"),
        password=require_env("QA_A_ADMIN_PASSWORD"),
    )


def employee_user_a() -> TenantUser:
    """A disposable QA user in Tenant A holding only the plain, self-service `Employee` role —
    i.e. no `Employee.View`/`Employee.Create`. Used for authorization-denial checks (EMP-SEC-002).

    Reads QA_A_EMPLOYEE_USERNAME / QA_A_EMPLOYEE_PASSWORD. Skips — never fabricates — when unset.
    """
    return TenantUser(
        identifier=require_env("QA_A_EMPLOYEE_USERNAME"),
        password=require_env("QA_A_EMPLOYEE_PASSWORD"),
    )


def manager_user_a() -> TenantUser:
    """A disposable QA user in Tenant A holding the `Manager` role (grants `Leave.Approve`, among
    other read-only/team-scoped permissions — see Backend/HRMS.Infrastructure/Persistence/Seed/
    SeedData.cs RolePermissionMap[RoleNames.Manager]). Used for the Leave approval-inbox checks.

    Reads QA_A_MANAGER_USERNAME / QA_A_MANAGER_PASSWORD. Skips — never fabricates — when unset.
    """
    return TenantUser(
        identifier=require_env("QA_A_MANAGER_USERNAME"),
        password=require_env("QA_A_MANAGER_PASSWORD"),
    )


def random_suffix(length: int = 8) -> str:
    """A short unique token for building disposable, non-colliding test data values."""
    return uuid.uuid4().hex[:length]


def unique_employee_code(prefix: str = TEST_DATA_MARKER) -> str:
    """A unique Employee Code matching CodeFormats.Pattern (alnum + . _ - /, max 20 chars)."""
    return f"{prefix}-{random_suffix(10)}"[:20]


def unique_employee_email(prefix: str = "qaauto") -> str:
    return f"{prefix}.{random_suffix(10)}@qa-automation.invalid"


def unique_last_name(prefix: str = TEST_DATA_MARKER) -> str:
    """A last name carrying a unique token, so a test can search for exactly the record it made
    without depending on (or colliding with) whatever else exists in the tenant's directory."""
    return f"{prefix}Smoke{random_suffix(8)}"


def unique_reason(kind: str, prefix: str = TEST_DATA_MARKER) -> str:
    """A Regularization/On Duty reason string carrying a unique token, so a request this suite
    submits is unmistakably its own and easy to find by hand if a cleanup step is ever skipped."""
    return f"{prefix} {kind} smoke {random_suffix(8)}"


def today_iso() -> str:
    return datetime.date.today().isoformat()


def unique_code(prefix: str = TEST_DATA_MARKER, length: int = 8) -> str:
    """A short unique alnum code for Payroll master data (Salary Component/Structure/Period/GL
    Account/Loan Product/Variable Pay Plan codes, etc.) — these are typically uppercased and
    stored as a natural key, so a fresh random suffix keeps parallel runs (`-n auto`) collision-free
    without depending on (or ever mutating) any pre-existing tenant configuration."""
    return f"{prefix}-{random_suffix(length)}".upper()
