# Module 13 — Statutory Compliance (Statutory Configuration and Effective-Dated Versions, Employee
Statutory Profile, PF/ESI/Professional-Tax/Income-Tax Contribution Calculation, Payroll Integration
and Retro/Recalculation Impact, Compliance Periods and Return Batches, the Unused Statutory Challan
Feature, Statutory Filing External Integrations, Authorization/Scope, Tenant Isolation, Audit/History,
Concurrency/Idempotency, SQL Server/MySQL Provider Parity, Large Data/Paging, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 13-statutory-compliance` regenerates the module's CSVs/coverage stats, and a full run across
all thirteen modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No automation implemented
yet (Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## Scope decision — read this before extending this module

This module merges three phases that together make up "Statutory Compliance" as a single QA unit:
phase 7F (`StatutoryConfiguration`/`StatutoryConfigurationVersion`/`StatutorySlab`/
`StatutoryComponentBasis`/`EmployeeStatutoryProfile` and the `StatutoryPayrollService.CalculateAsync`
contribution engine wired into payroll), phase 7K (`PayrollCompliancePeriod`/
`PayrollStatutoryReturnBatch` register generation and its Generate→Validate→Approve→Export→MarkFiled
lifecycle, plus the never-used `PayrollStatutoryChallan`), and phase 7X (`StatutoryFilingDefinition`/
`ConnectionProfile`/`Run`/`Package`/`Submission`/`Acknowledgement` external-filing lifecycle and its
connectors). These three phases share one calculation/data backbone (`PayrollStatutoryResult` →
`PayrollStatutoryReturnEmployee` → `StatutoryFilingRunItem`) and were read together as one coherent
"statutory compliance" surface, matching the scope the task was given.

**Explicitly out of scope for this module** (present and real in the codebase, not covered here, and
not claimed as covered): the full annual income-tax/TDS computation engine — regime selection,
investment declarations and proof verification, Form 16, year-end reconciliation
(`YearEndTaxService.cs`, `TaxDeclarations.cs`, phases 7U/7W). This is the identical scope-boundary
shape as `qa/10-payroll` excluding Payroll Accounting/GL Posting and `qa/12-bonus-variable-pay`
excluding GL journal generation: a separate, self-contained concern with its own design doc. This
module covers only the `StatutoryType.IncomeTax` slab-based **monthly** withholding that lives inside
the generic `StatutoryPayrollService` calculation framework alongside PF/ESI/Professional Tax
(F-STAT-050 documents, source-verified, that these two income-tax code paths never call each other in
either direction). Payroll Accounting/GL posting of the `StatutoryLiability` mapping type (phase 7I) is
likewise out of scope for the identical reason. Gratuity/separation-benefit calculation (phase 7P) is
an unrelated module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (152 functionalities: F-STAT-001…152). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-configuration-versions.yaml` … `cases/15-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. |
| `clarifications.yaml` | The CR-219…238 QA-risk register for this module (continuing after `qa/12-bonus-variable-pay`'s CR-218). |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 13-statutory-compliance`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Modules 8–12, no case in this module cites an `ex:` existing-test reference (see below for why).

**Build note:** `build_test_catalogue.py` overwrites `qa/HRMS_Test_Cases.xlsx` in full on every run —
it is not additive. Running it with `--module 13-statutory-compliance` alone regenerates *this
module's own* per-module CSVs/coverage-stats.json correctly, but if that single-module invocation is
the last one run, the saved workbook will contain **only** this module's sheets. Always follow a
single-module run with a full, no-argument run (`python qa/tools/build_test_catalogue.py`) before
treating `HRMS_Test_Cases.xlsx` as current — this is exactly what was done to produce the workbook
shipped with this module (all thirteen modules' sheets are present, 2,923 manual cases in total).

## The real implementation (source-verified by direct code reading, not assumed from any design doc)

Discovered by reading, in full: `StatutoryPayrollController.cs`, `PayrollStatutoryComplianceController`
(nested inside `PayrollOutputsController.cs`), and `StatutoryFilingsController.cs` (every route);
`StatutoryPayrollService.cs`, `PayrollStatutoryComplianceService.cs`, `StatutoryFilingService.cs`, and
`StatutoryFilingConnectors.cs`; `PayrollApprovalGuard.cs` (the shared, opt-in self-approval guard used
by both compliance-return and filing-run Approve); the `statutory.CalculateAsync` call sites inside
`PayrollCalculationEngine.cs`'s `CalculateEmployeeAsync`/`CalculateAdjustmentOnlyAsync`;
`IStatutoryPayrollService.cs`/`IPayrollStatutoryComplianceService.cs`/`IStatutoryFilingService.cs`;
`StatutoryDtos.cs`; the `StatutoryConfiguration`/`EmployeeStatutoryProfile`/`PayrollStatutoryResult`
family in `PayrollCalculation.cs`; the full `PayrollStatutoryCompliance.cs` (including the unused
`PayrollStatutoryChallan`); the full, 12-entity `StatutoryFiling.cs`; every statutory-related enum in
`PayrollEnums.cs` (confirming `StatutoryType` has exactly four members, no Labour Welfare Fund);
`Permissions.cs`'s 29-permission `Payroll.Statutory*`/`StatutoryCompliance*`/`StatutoryFiling*` block;
`SeedData.cs`'s `RolePermissionMap`, grep-verified for every one of those 29 constants; every relevant
EF configuration and its index/concurrency-token setup; migration pairs confirmed present in both the
SQL Server and MySQL chains; `StatutoryConfigurationsPage.tsx`, `StatutoryCompliancePage.tsx` (+ its
`.test.tsx`), `StatutoryFilingsPage.tsx`, `src/api/payroll.ts`'s statutory exports, and
`src/api/statutoryFilings.ts`; `PersonalDetailsForm.tsx`'s unrelated legacy "Statutory & Benefits"
section; `navigation.ts`/`App.tsx` route registrations; and the existing 13-file xUnit inventory
(`StatutoryCalculationTests`, `SqlServerStatutoryPayrollIntegrationTests`,
`MySqlStatutoryPayrollIntegrationTests`, `PayrollStatutoryComplianceTests`,
`SqlServerPayrollStatutoryComplianceIntegrationTests`,
`MySqlPayrollStatutoryComplianceIntegrationTests`, `PayrollStatutoryComplianceProviderAcceptance`,
`StatutoryFilingFoundationTests`, `StatutoryFilingConnectorTests`, `StatutoryFilingConcurrencyTests`,
`StatutoryFilingRetrySafetyTests`, `StatutoryFilingLargeDataTests`,
`StatutoryFilingProviderAcceptance` — referenced narratively per the `qa/10-11-12` precedent, not
individually classified). `docs/phase-7f-statutory-calculation-framework.md`,
`docs/phase-7k-payroll-statutory-compliance-returns.md`, and
`docs/phase-7x-statutory-filing-external-integrations.md` were read for scope framing only; current
code wins wherever a doc and the code diverge, per `CLAUDE.md`.

**HEADLINE FINDING — recalculation silently corrupts both the results endpoint and the compliance
register (CR-219).** `PayrollStatutoryResult` has no `CalculationAttemptId`/`IsCurrent` field, unlike
`PayrollResult`/`PayrollResultComponent`/`PayrollCalculationError`. Recalculating a payroll run never
deletes or supersedes a prior attempt's statutory rows — they accumulate forever. `GetResultsAsync`'s
own query has no attempt filter at all, so it returns the union of every retained attempt after even a
single recalculation, and `PayrollStatutoryComplianceService.GenerateAsync`'s own source query inherits
the identical defect, meaning a compliance return generated after a recalculation can double- or
multiple-count an employee's contribution — a headline correctness defect touching real money figures
reported to a regulator.

**A materially larger permission gap, the identical shape to `qa/11-loans-advances`' CR-183 and
`qa/12-bonus-variable-pay`'s CR-202 (CR-220).** A full grep of `SeedData.cs`'s `RolePermissionMap` for
all 29 `Payroll.Statutory*`/`StatutoryCompliance*`/`StatutoryFiling*` permission constants returns
matches only inside the id-numbering dictionary and `Permissions.All` — **zero matches inside any
individual role's grant array**. Only SuperAdmin/TenantAdmin hold any of this module's permissions out
of the box, including an employee's own `EmployeeStatutory.View`.

**A completely dead feature (CR-221).** `PayrollStatutoryChallan` is a fully migrated entity, table,
and EF configuration that no controller, service, DTO, or frontend code anywhere ever creates, reads,
updates, or lists (94-file grep, every match an entity/config/migration file).

**A confirmed data-quality defect specific to Professional Tax (CR-224).**
`PayrollStatutoryReturnEmployee.PtRegistrationReference` is declared specifically for the PT compliance
export column but is never assigned anywhere in `GenerateAsync` — every PT return's identifier column
is permanently empty.

**Two confirmed, severe frontend gaps (CR-234, CR-235).** `EmployeeStatutoryProfile` — the record that
actually gates whether PF/ESI/PT/IncomeTax is calculated at all — has **zero UI surface anywhere**,
even though its client API functions already exist; in its place, `PersonalDetailsForm.tsx` renders an
unrelated, older "Statutory & Benefits" section whose fields have no effect on calculation eligibility
whatsoever. Separately, `StatutoryCompliancePage` exposes only 2 of 6 lifecycle actions (with no
ComplianceType/jurisdiction selector at all) and `StatutoryFilingsPage` exposes only 2 of 8, leaving
every real operational action beyond Generate/Validate reachable only via a direct API call — the
identical shape as `qa/12-bonus-variable-pay`'s CR-207.

**An explicitly documented, intentional design choice, not a gap (CR-222).** Only
`ManualDownloadStatutoryFilingConnector` and `TestStatutoryFilingConnector` are ever registered — there
is no real EPFO/ESIC/GSTN/TRACES government-portal integration anywhere, a fact the product UI itself
discloses to the end user. A closely related, genuinely open finding (CR-223) is that `ManualDownload`'s
`SubmitAsync` unconditionally reports `Outcome=Pending`, which the service then maps to `Status=
Submitted` — a status name that, for this connector alone, does not mean anything was actually
transmitted.

**Several declared-but-unenforced/dead configuration and validation paths, the same shape as prior
modules' CR-178/CR-186/CR-193/CR-200/CR-214.** No `UpdateConfigurationAsync`/`DeactivateAsync` exists
for `StatutoryConfiguration` (CR-227). Statutory-version and employee-profile overlap checks are
app-level-only, backed by a non-unique index (CR-228). `ValidateAsync`'s own "Return contains validation
errors" guard (CR-229) and `StatutoryFilingService.ValidateAsync`'s sole `MissingSource` rule (CR-230)
are both unreachable dead code against normally-generated data. No Cancel action exists for a
`PayrollStatutoryReturnBatch` despite `Cancelled` being an actively-referenced status (CR-231). Retry
eligibility for a failed filing submission is gated on one connector's own hardcoded response-code
string (CR-232). Both statutory-return and statutory-filing self-approval prevention are opt-in only,
disabled by default (CR-233). Labour Welfare Fund has no `StatutoryType` member at all, despite being a
selectable descriptive tag on a salary component (CR-226). `EmployeeStatutoryProfile.Uan/EsiNumber` and
`Employee.UanNumber/EsicNumber` are two independent, never-reconciled identifier pairs, and the
compliance register sources from the latter, not the former (CR-225).

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Statutory Configuration and Versions | Full CRUD-minus-Update, validation matrix, ANY-status overlap rule, unvalidated rule ranges | Priority/version-count edge cases | No Update/Deactivate (CR-227); overlap race window (CR-228) |
| Employee Statutory Profile | Append-only save, overlap rejection, normalisation, audit history | Cross-tenant, IsActive gating | Zero UI surface (CR-234); identifier divergence from Personal Details (CR-225) |
| Contribution Calculation Engine | No-profile no-op, profile selection, config/version matching, ambiguity abort, basis resolution (incl. PF/Esi-vs-PT/IncomeTax null-basis divergence), ceiling clamp, PF/Esi rate math, PT/IncomeTax slab math with OptionalMonth, rounding, LWF's total absence | Metadata shape, uniqueness, Forbidden-vs-NotFound quirk | **Silent skip on missing config/slab (F-STAT-037/044) is by far the most consequential "no error" path in the module** |
| Payroll Integration and Retro/Recalculation Impact | Regular + off-cycle integration, NegativeNetPay interplay, the full recalculation-accumulation proof (response vs. DB vs. compliance-return double-count) | Retro-period effective-config resolution | **CR-219 headline defect, proven end-to-end** |
| Compliance Periods and Return Batches | Full Generate→Validate→Approve→Export→MarkFiled lifecycle, idempotency, aggregation, the always-null PT identifier, the always-Valid ValidationStatus dead path | Batch numbering, unpaginated register | No Cancel (CR-231); PT identifier bug (CR-224); source divergence (CR-225) |
| Statutory Challans | Schema existence proof, complete-non-usage proof | — | **CR-221, entirely dead feature** |
| Statutory Filing — Setup | Definition/connection CRUD, immediate-usability contrast with Configuration, advisory-only validation, secret non-disclosure | Run creation idempotency | — |
| Statutory Filing — Lifecycle | Full Generate→Validate→SubmitForApproval→Approve→Submit→Reject/Acknowledge/Cancel state machine, content-hash dedup/resubmission versioning, the two-connector proof, the hardcoded-retry-string proof | Concurrency-race degradation | **CR-222 (by design)/CR-223 (Submitted≠transmitted)/CR-232 (retry string)** |
| Authorization and Scope | Full unroutable-permission audit, the seeded-role absence proof, combined-permission self-approval proof, ManageConnections separation proof | — | **CR-220 headline finding** |
| Tenant Isolation | Cross-tenant NotFound proof across all three services, composite-key proof, code-reuse-across-tenants proof | Query-filter bypass audit | — |
| Audit and History | Append-only proof across all four history tables, PreviousStatus/NewStatus richness contrast, the no-GET-route proof | — | Audit trail queryable only by DB access (F-STAT-128) |
| Concurrency and Idempotency | Overlap race windows (config version + profile), dormant vs. genuinely-enforced concurrency tokens, DB-enforced package/submission uniqueness | — | Mixed rigor: some CAS genuinely enforced, some app-level-only |
| SQL Server/MySQL Provider Parity | Migration-pair proof, existing provider-fixture inventory | — | **No Filing-area provider fixture exists (CR-238)** |
| Large Data and Paging | Configuration-list clamping/stability | Filing large-data fixture existence, unpaginated compliance register | Return register has no paging at all |
| Frontend | Missing-version-UI proof, minimal-lifecycle proof (both compliance and filing pages), missing-profile-UI proof, PersonalDetailsForm confusion proof, nav-gap proof, permission-gating contrast | — | **CR-234/CR-235, the two most severe frontend findings in this module** |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| Stat_Configuration | 15 | Configuration/version CRUD-minus-Update, ANY-status overlap, unvalidated rules, no-transition gap |
| Stat_EmployeeProfile | 10 | Append-only profile save, overlap, normalisation, identifier divergence, missing UI |
| Stat_Calculation | 16 | Profile/config matching, ambiguity, basis resolution, ceiling, PF/Esi/PT/IncomeTax math, LWF absence, TDS-engine independence |
| Stat_PayrollIntegration | 8 | Regular/off-cycle integration, NegativeNetPay, the full recalculation-accumulation proof |
| Stat_ComplianceReturns | 16 | Period/return lifecycle, idempotency, PT identifier bug, source divergence, no-Cancel gap |
| Stat_Challans | 3 | Schema-exists-but-unused proof |
| Stat_Filing_Setup | 6 | Definition/connection CRUD, immediate usability, advisory validation, secret handling |
| Stat_Filing_Lifecycle | 13 | Full filing state machine, dedup/resubmission, connector proof, hardcoded retry string |
| Stat_Authz_Scope | 7 | Unroutable-permission audit, seeded-role absence, self-approval, ManageConnections separation |
| Stat_Tenant_Isolation | 4 | Cross-tenant proof, composite keys, code reuse across tenants |
| Stat_Audit_History | 6 | Append-only proof, richness contrast, no-GET-route proof |
| Stat_Concurrency | 7 | Overlap races, dormant vs. enforced concurrency tokens, DB-enforced uniqueness |
| Stat_Provider_Parity | 4 | Migration pairing, existing fixture inventory, Filing-area test gap |
| Stat_Large_Data | 3 | Configuration-list clamping, filing/compliance scale-fixture contrast |
| Stat_Frontend | 9 | Missing-version UI, minimal lifecycle UI (both pages), missing-profile UI, PersonalDetailsForm confusion, nav gap |

**Total: 127 test cases, 152 functionalities** (`python qa/tools/build_test_catalogue.py --module
13-statutory-compliance` regenerates and reports the authoritative counts; treat `coverage-stats.json`
as authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-219**: **Confirmed defect (headline)** — `PayrollStatutoryResult` has no attempt-scoping; recalculation accumulates stale rows and corrupts both the results endpoint and compliance-return generation.
- **CR-220**: **Headline finding** — no seeded role but SuperAdmin/TenantAdmin holds any of the 29 statutory permissions.
- **CR-221**: `PayrollStatutoryChallan` is a fully migrated, completely unused entity.
- **CR-222**: (by design, documented in the UI) only ManualDownload/Test filing connectors exist.
- **CR-223**: ManualDownload's "Submitted" status does not mean anything was actually transmitted.
- **CR-224**: **Confirmed defect** — `PtRegistrationReference` is never populated for Professional Tax returns.
- **CR-225**: `EmployeeStatutoryProfile`'s own Uan/EsiNumber never reconcile with the legacy Employee fields the compliance register actually uses.
- **CR-226**: Labour Welfare Fund has no `StatutoryType` member and thus no calculation-engine support.
- **CR-227**: No Update/Deactivate exists for `StatutoryConfiguration`.
- **CR-228**: Version/profile overlap checks are app-level-only, with a genuine race window.
- **CR-229**: The compliance-return `ValidationStatus=Invalid` guard is unreachable dead code.
- **CR-230**: The filing run's sole validation rule is unreachable against normally-generated data.
- **CR-231**: No Cancel action exists for a `PayrollStatutoryReturnBatch`.
- **CR-232**: Filing submission retry is gated on one connector's own hardcoded response-code string.
- **CR-233**: Self-approval prevention for both statutory-return and statutory-filing Approve is opt-in only.
- **CR-234**: **Confirmed defect (frontend, headline)** — `EmployeeStatutoryProfile` has zero UI surface; an unrelated legacy section sits in its place.
- **CR-235**: **Confirmed defect (frontend, headline)** — both statutory pages are missing the majority of their own lifecycle actions.
- **CR-236**: Two of three statutory pages have no `navigation.ts` entry.
- **CR-237**: `StatutoryCompliancePage` has no permission gating on its buttons at all.
- **CR-238**: No SQL Server/MySQL provider-specific test class exists for the Filing area.

Only CR-219, CR-224, CR-234, and CR-235 are asserted as confirmed defects; CR-222 documents an
explicitly-disclosed, intentional design choice (no change requested), the same shape as
`qa/12-bonus-variable-pay`'s CR-214. Every other CR is classified `Open - awaiting decision`, per the
classification column in `clarifications.yaml` — each is exposed by specific test cases and awaits
engineering/product action.

## Existing automated coverage referenced by this module

Statutory Compliance has the largest existing xUnit inventory of any module surveyed so far in this QA
programme (13 files: `StatutoryCalculationTests`, the four SQL Server/MySQL provider-specific pairs for
the Calculation Framework and Compliance Returns areas, `PayrollStatutoryComplianceTests`,
`PayrollStatutoryComplianceProviderAcceptance`, and five dedicated Filing-area files covering
foundation, connectors, concurrency, retry-safety, and large-data scale). Following the identical
precedent set by Modules 8–12, **no case in this module cites an `ex:` existing-test reference.** Every
existing test class discovered during research is instead documented narratively inside the relevant
`src:` fields, the coverage matrix above, and `clarifications.yaml`; `qa/tools/existing_test_layers.yaml`
was **not** extended for this module, consistent with `qa/tools/build_test_catalogue.py`'s validation
(which only requires `ex:` references to resolve when they are actually used).

A green run of `SqlServerStatutoryPayrollIntegrationTests`/`MySqlStatutoryPayrollIntegrationTests` and
`SqlServerPayrollStatutoryComplianceIntegrationTests`/`MySqlPayrollStatutoryComplianceIntegrationTests`
is evidence of provider parity for the Calculation Framework and Compliance Returns areas only — **it
is explicitly not evidence of Filing-area parity** (CR-238), since no equivalent provider-specific pair
exists for Filing; only the generic `StatutoryFilingProviderAcceptance.cs` covers that area, and its own
exact scope (which of Generate/Validate/Submit/Approve/Reject/Acknowledge/retry/resubmission it
actually exercises against a real provider) is left for execution-time confirmation per `STAT-PROV-003`.
