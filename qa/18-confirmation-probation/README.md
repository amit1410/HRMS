# Module 18 — Confirmation & Probation Policy (Phase 9a foundation)

Configurable, per-tenant confirmation & probation policies. A policy owns effective-dated versions;
each version carries one probation configuration (duration, scheduler trigger window, extension policy)
and zero or more applicability rows. The applicable policy for an employee is resolved by **priority →
specificity → effective date** (identical to the leave-policy resolver), and the Confirmation Date is
**derived from the applicable policy** rather than maintained by hand on the employment record. Employees
with no applicable policy keep their existing manual confirmation data unchanged.

## Scope in 9a (this suite)

- Policy master CRUD + effective-dated draft/publish version lifecycle (`ConfirmationPoliciesController`).
- Probation configuration and applicability (Holding Company, LOB, Organisation, Department, …, Grade,
  Designation, Employee Type, Location, Country, Cost Center, Gender).
- Read-side resolution (`/resolve`) and per-employee confirmation status (`/employees/{id}/status`).
- Employment integration: Confirmation Date becomes read-only / policy-managed when a policy applies.
- Authorization: `Confirmation.PolicyView` / `PolicyManage` / `PolicyPublish`.

## Phase 9c-1 (this suite, added)

Confirmation **review-question** and **rating-scale** masters — the configuration foundation for the 9c
review/decision workflow. Both are tenant-configurable and never hard-coded:

- Rating-scale master CRUD (`ConfirmationRatingScalesController`): code/name, min/max bounds, and ordered
  value labels (e.g. 1 = Unsatisfactory … 5 = Outstanding). The numeric bounds + question→scale link are the
  hook that 9c-3 decision rules (overall rating ≥ X → Confirm) will read.
- Review-question master CRUD (`ConfirmationReviewQuestionsController`): question text/help, type
  (Rating / YesNo / Text / Dropdown / MultiSelect), mandatory, active, display order, effective dates,
  options (for Dropdown/MultiSelect), a rating-scale reference (for Rating), and applicability rows that reuse
  the confirmation-policy dimensions plus an optional policy association.
- Authorization: `Confirmation.ReviewQuestionView` / `ReviewQuestionManage` and
  `Confirmation.RatingScaleView` / `RatingScaleManage`.
- Cases: `cases/03-rating-scales.yaml`, `cases/04-review-questions.yaml`. Automation:
  `qa/automation/tests/api/test_confirmation_review_smoke.py` (helpers in
  `qa/automation/core/confirmation_review_api.py`).

9c-1 is masters only — it never stores review answers, and never touches Confirmation Date, Job Status or the
9b `ConfirmationReminderDelivery` model.

## Phase 9c-2 (this suite, added)

Confirmation **review cases, question resolution, snapshotting and reviewer reviews** — the data-collection
half of the review workflow (`ConfirmationReviewCasesController`). A review case is created for an employee's
probation cycle; the applicable 9c-1 questions are **resolved** and **snapshotted** onto the case so later
master edits never change a historical review; the Reporting Manager and HR/HRBP each save a draft and submit.

- **Question resolution** (`ConfirmationReviewQuestionResolver`): reuses the 9a applicability match/specificity
  philosophy. A question is included when it is active + effective-dated **and** (has no applicability sets —
  a tenant-wide generic question — or has a set whose `ConfirmationPolicyId` is null/equals the resolved
  policy **and** whose populated dimensions all match the employee's effective employment). Applicable
  questions are unioned, de-duplicated by id, and ordered by DisplayOrder → Code → Id (deterministic).
- **Snapshot** (audit-critical): at case creation the question text, code, type, mandatory flag, display order,
  options and rating-scale values are copied into case-owned tables (`ConfirmationReviewCaseQuestions`/
  `…Options`/`…RatingValues`). Nothing ever updates them, so a later master edit cannot alter a created case.
- **Reviewers**: Reporting Manager is resolved via the existing `IEmployeeManagerResolver`; HR/HRBP is derived
  from HRBP-role assignment scopes covering the employee (0/ambiguous ⇒ unassigned, recorded in the audit
  trail; HR/HRBP submission is then authorized by permission + org scope). Creation is **idempotent** per
  employee + probation cycle.
- **Reviews**: a reviewer saves a draft (partial allowed) and submits (mandatory enforced; answers validated
  against the **snapshot** — rating ∈ snapshot scale, dropdown/multiselect ∈ snapshot options). A submitted
  review is immutable — a correction creates a new version and supersedes the prior one. Every mutation appends
  a `ConfirmationReviewCaseEvent` (CaseCreated, QuestionsSnapshotted, ReviewersResolved, DraftSaved,
  ReviewSubmitted, ReviewSuperseded).
- Authorization: `Confirmation.ReviewCaseView` / `ReviewCaseManage` / `ReviewManagerSubmit` / `ReviewHrbpSubmit`,
  enforced at the API level and, for submission, additionally scoped by `EmployeeAccessScopeService`.
- Cases: `cases/05-review-cases.yaml`. Automation:
  `qa/automation/tests/api/test_confirmation_review_case_smoke.py` (helpers in
  `qa/automation/core/confirmation_review_case_api.py`).

9c-2 collects reviews only — it makes **no** confirm/extend/reject decision, no auto-confirmation, and changes
no `JobStatus` / `ConfirmationDate` / `DateOfJoining`. Those belong to Phase 9c-3.

## Not in 9a / 9c-1 / 9c-2 (follow-ups)

- **9b** — scheduler (BackgroundService) + notifications to Employee / Reporting Manager / HRBP, with a
  claim/dedup delivery+execution log; scheduler-config screen.
- **9c-3+** — configurable approvals (Manager/HRBP/both) with manual outcome (Confirm/Extend/Reject/Pending),
  auto-confirmation writing Confirmation Date / Job Status with audit + extension recalculation, and the
  dashboard. Permissions `Confirmation.ReviewSubmit` / `Decide` / `DashboardView` are seeded, reserved for
  those sub-phases. (9c-2 — review cases + question resolver + snapshot + reviewer reviews — is now shipped;
  see above.)

## Automation

`qa/automation/tests/api/test_confirmation_smoke.py` (helpers in `qa/automation/core/confirmation_api.py`).
Requires `QA_TENANT_A_HOST`, `QA_A_ADMIN_*` (manage/publish) and `QA_A_EMPLOYEE_*` (neither). Policies are
created with unique codes (there is no delete endpoint in 9a); the suite never mutates another tenant's data.
