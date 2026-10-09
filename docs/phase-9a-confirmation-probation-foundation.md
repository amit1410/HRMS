# Phase 9a — Confirmation & Probation Policy (Foundation)

## Goal

Let each tenant configure its own **confirmation & probation policy** so the system derives an employee's
probation end and confirmation status from policy, instead of HR hand-maintaining a Confirmation Date on the
employment record. 9a delivers the policy master, the applicability resolver, policy-driven confirmation-date
calculation, migrations, permissions, admin UI, tests and QA. The scheduler (9b) and the review/decision
workflow with auto-confirmation (9c) build on this foundation.

## Design decisions

- **Mirror the leave-policy foundation.** The policy → version → applicability-set shape and the
  priority → specificity → effective-date winner algorithm are intentionally identical to
  `LeavePolicy`/`LeavePolicyResolver`, reusing the same `EffectiveEmploymentResolver` snapshot. This keeps
  one mental model for "configurable policy with applicability" across the product.
- **Date of Joining is the single source.** Probation end = `Employee.DateOfJoining` + probation duration.
  No confirmation-related date is duplicated onto employment (`EmployeeEmployment.ConfirmationDate` already
  existed and remains the only confirmation date; `Employee.DateOfJoining` is the only DOJ).
- **Policy-owned with manual fallback.** When a policy resolves, the employment read reports
  `ConfirmationDateIsPolicyManaged = true` + the computed probation end, and the UI makes the field
  read-only; a manual Confirmation Date change is rejected. Employees with **no** applicable policy keep
  their existing manual value and behaviour, so existing tenants are unaffected.
- **Configurable approvals, manual outcome (9c).** The probation rule reserves `RequiredApproval`,
  `ConfirmRatingThreshold`, `AutoConfirmOnApproval` (nullable/defaulted) so 9a's schema is stable; the
  decision workflow is manual-outcome with configurable approvers, not an auto-decision engine.

## Data model (tenant chains)

| Table | Purpose |
|---|---|
| `ConfirmationPolicies` | Tenant policy header (Code, Name, Description, IsActive). |
| `ConfirmationPolicyVersions` | Effective-dated version (EffectiveFrom/To, Status draft/published/retired, Priority). |
| `ConfirmationPolicyRules` | 1:1 with a version — probation duration + unit, trigger days, extension policy; reserved 9c approval/threshold fields. |
| `ConfirmationPolicyApplicabilitySets` | Applicability dimensions (same list as `LeavePolicyApplicabilitySet`). |

Composite `(TenantId, Id)` alternate keys and tenant-owned composite FKs throughout; `Country` is the one
global reference (no tenant component), matching the leave configuration. Migrations added to both tenant
chains (`HRMS.Infrastructure/Persistence/Migrations` for SQL Server, `HRMS.Infrastructure.MySqlMigrations`
for MySQL) + their model snapshots. The catalog chain is unaffected (all entities are tenant-scoped).

## Resolution

`ConfirmationPolicyResolver.ResolveAsync(tenant, employee, date)`:
1. Resolve the employee's effective employment snapshot on `date` (`EffectiveEmploymentResolver`).
2. Consider published versions of active policies whose effective range covers `date` and that have a
   probation rule.
3. For each, match applicability sets against the snapshot; specificity = count of populated dimensions
   (zero sets = tenant-wide default, specificity 0; sets present but none match = excluded).
4. Winner = highest Priority, then greatest specificity; ties or two published versions of one policy on the
   same date are reported as `ConfigurationAmbiguity`. No candidate ⇒ `NoPolicy` (caller falls back to manual).

Probation end is computed by `ConfirmationDateCalculator` (months added calendar-wise, days directly); the
scheduler trigger date (consumed in 9b) is probation end − trigger window, never before the Date of Joining.

## API (`ConfirmationPoliciesController`, `/api/confirmation-policies`)

List / get / create / update policy; list versions; editor; create/update draft version; publish; `resolve`;
`employees/{id}/status`. Thin controller, `Result → ToActionResult`, `[HasPermission]` per endpoint.

## Authorization

New permissions `Confirmation.PolicyView|PolicyManage|PolicyPublish` (9a) and reserved
`ReviewSubmit|Decide|DashboardView` (9b/9c), all in `Permissions.All`, seeded with role grants in
`SeedData` (HRAdmin: manage/publish; HRManager/Manager: view/review), and mirrored in the frontend
`permissions.ts` (the C#-reading parity test enforces name + order).

## Frontend

`src/pages/confirmation/ConfirmationPoliciesPage` (list + create/edit) and `ConfirmationPolicyEditorPage`
(version + probation configuration + applicability/criteria), routed under `/confirmation/policies`, nav
entry under Employee Management gated by `Confirmation.PolicyView`. `EmploymentSectionForm` renders the
Confirmation Date read-only with a policy hint when `confirmationDateIsPolicyManaged`.

## Tests

- Backend `ConfirmationPolicyTests`: calculator, resolver (no-policy / tenant-wide default / priority &
  specificity / ambiguity), service lifecycle & resolve, employee-status managed vs fallback.
- Frontend `ConfirmationPoliciesPage.test.tsx` + the permissions parity test.
- QA `qa/automation/tests/api/test_confirmation_smoke.py` + `qa/18-confirmation-probation/cases`.

SQLite (default suite) proves logic, not provider behaviour; run the MySQL/SQL Server provider suites for
rowversion/locking/collation parity before release.

## Follow-ups

- **9b** — `ConfirmationReminderWorker` + `ConfirmationReminderProcessor` (mirror the leave reminder worker)
  with a claim/dedup delivery+execution-log table; notify Employee + Reporting/L1 Manager + HRBP.
- **9c** — dynamic review-question master, rating scale, review responses + audit, configurable approvals with
  manual outcome, auto-confirmation (writes `ConfirmationDate`/`JobStatus` with audit + extension
  recalculation), dashboard and review-form screens.
