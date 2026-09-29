# Module 4 — Employee Code Generation

Status: manual test-case catalogue complete and validated. No automation implemented yet (Playwright is
Phase 8+, on explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (52 functionalities: F-CODE-01…52). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-configuration-mode.yaml` … `cases/09-frontend.yaml` | The manual test cases, one YAML file per Excel worksheet (`sheet:` key), one case per `id`. |
| `clarifications.yaml` | The CR-37…CR-46 QA-risk register for this module (numbering continues after Module 3's CR-36). |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here.

## The real implementation (source-verified, not assumed from `docs/`)

Discovered by reading `EmployeeCodeConfigurationController`/`Service`, `EmployeeCodeSequenceService`,
`EmployeeCodeRuleMatcher`/`EmployeeCodeRenderer`, `EmployeeEmploymentService.CreateChangeAsync`/
`AssignPendingEmployeeCodeAsync`, every `EmployeeCode*` entity and EF configuration, the two per-provider
`IEmployeeCodeSequenceUpdater` implementations, the four `AddEmployeeCode*`/`AllowPendingEmployeeCode`
migrations, `SeedData.RolePermissionMap`, and the frontend `EmployeeCodeConfigurationPage.tsx`,
`EmploymentSectionForm.tsx` and `PersonalDetailsForm.tsx`.

**There are two Employee-Code implementations in this codebase, only one of which is live:**

1. **The live engine** — `EmployeeCodeConfig`/`EmployeeCodeConfigVersion` (+ `EmployeeCodeRule`/`Condition`/
   `Segment`/`Sequence`), read and written exclusively through `EmployeeCodeConfigurationController`
   (`/api/employee-code-configuration[/rules|/preview]`), and consumed exclusively by
   `EmployeeEmploymentService.CreateChangeAsync` — the single place a code is ever assigned, and only once,
   at an employee's first employment record (`employee.EmployeeCode is null` gate). Two independent axes:
   `AssignmentMode` (Manual/Auto) × `GenerationMethod` (Simple/RuleBased, Auto only). Simple composes
   `Prefix + Separator + PaddedNumber` off a per-version counter; Rule-Based matches the lowest-`Priority`
   Active rule whose conditions all match (falling back to the version's single Active default rule) and
   renders an ordered segment list (fixed text, master codes, a per-rule sequence, joining year/month,
   custom constants) via a separate, per-rule `EmployeeCodeSequence` row.
2. **A legacy, dead implementation** — `EmployeeService.cs`'s `ResolveEmployeeCodeAsync`/
   `PersistEmployeeCodeCounterAsync`, a simpler `Prefix+NextNumber` composer that reads/writes
   `EmployeeCodeConfig`'s own mirrored fields directly. It has **zero callers anywhere in the solution**
   (CR-37) — `CreatePersonalDetailsCoreAsync` sets `EmployeeCode = null` unconditionally and defers to
   Initial Employment.

**Effective dating** is per-`EmployeeCodeConfigVersion` (`EffectiveFrom`/`EffectiveTo`/`IsActive`), with an
interval-overlap guard on save against every other *active* version, and employment-time resolution keyed
off the employment record's own `EffectiveFrom` (never "today") — so a backdated hire correctly resolves an
otherwise-expired version, and a gap between two versions cleanly fails generation rather than defaulting.

**Rule matching** is AND-of-conditions, `Equals`-only (`EmployeeCodeConditionOperator` has exactly one
member — CR-43), by `ReferenceId` when present or by a literal `Value` otherwise; a stale `Value` string
never affects matching once a `ReferenceId` is set, because the live master `Code` is always re-resolved
from current employment references. `Location` is modeled in every relevant enum but explicitly rejected at
both condition- and segment-save time, because this data model has no separate Location master.

**Sequence concurrency**: Rule-Based uses `EmployeeCodeSequenceService.AllocateAsync` (create-if-absent,
then an optimistic `ExecuteUpdateAsync`/`IEmployeeCodeSequenceUpdater` retry loop, `MaxAttempts=8`). Simple
mode has its own, separately-implemented retry loop directly against `EmployeeCodeConfigVersion.NextNumber`
and — unlike Rule-Based — has **no** duplicate-code guard against `Employees.EmployeeCode` before assigning
(CR-45). `SqlServerEmployeeCodeSequenceUpdater` and `MySqlEmployeeCodeSequenceUpdater` diverge exactly as
CLAUDE.md documents for this codebase (native rowversion vs. app-managed token) — but **no
provider-acceptance or dedicated concurrency test file exists for this feature at all** (CR-40), unlike
every other per-provider-concurrency subsystem in the repo.

**Authorization**: gated by `EmployeeCodeConfiguration.View`/`.Manage`. Per `SeedData.RolePermissionMap`,
only `SuperAdmin`/`TenantAdmin` (via `DomainPermissions.All`) hold either permission — **HRAdmin, the role
that actually performs Initial Employment and triggers code generation, holds neither** (CR-44).

## Coverage matrix — which behavior is exercised where

| Behavior | Fully exercised on | Spot-checked on | Kind-specific cases |
|---|---|---|---|
| Configuration save/mode switching | Auto/Simple, Manual | Legacy `autoGenerate` compatibility | Config-field validation matrix (06) |
| Simple Sequence composition | prefix/separator/padding/rollover | large (near-`long.MaxValue`) values | Missing duplicate guard, CR-45 |
| Rule-Based matching/rendering | priority, default fallback, AND-conditions | stale condition Value, hierarchy no-op (CR-42) | Location rejection, padding ceiling (CR-39) |
| Effective dating | exact boundaries, gaps, overlaps, backdated/future hires | overlapping-active data-integrity edge | version-copy-on-edit (rule versioning) |
| Employee/Employment integration | assign-once, never-regenerate (CR-41) | rollback on failure, DI-unavailable edge | dead legacy path (CR-37) |
| Configuration/rule validation | every FluentValidation + service-level rule | null-list DTO edge (CR-46), unknown-field condition | duplicate name/priority is allowed (no uniqueness) |
| Sequence concurrency | Rule-Based and Simple burst allocation (SQLite) | duplicate-config race (CR-38) | SQL Server/MySQL runs are `NOT EXECUTED` unless env vars set |
| Authorization / tenant isolation | full role matrix, HRAdmin gap (CR-44) | composite-FK cross-tenant rejection | mid-session permission revocation |
| Frontend | mode/method choice cards, rule builder, manual-code gating | — | no Vitest coverage exists yet (F-CODE-50) |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| EmployeeCode_Config | 31 | Assignment-mode save semantics, legacy `autoGenerate` compatibility, permission/tenant gates, and the full configuration-field validation grid |
| EmployeeCode_Sequence | 24 | Simple Sequence composition/padding/rollover, plus every sequence-concurrency and SQL Server/MySQL provider-parity case |
| EmployeeCode_Rules | 41 | Rule CRUD validation, matching/rendering, hierarchy checks, soft delete, version-copy-on-edit, and Preview |
| EmployeeCode_EffectiveDates | 18 | Version overlap detection, exact-boundary/gap/backdated/future-dated employment resolution |
| EmployeeCode_Integration | 21 | Employee creation → Initial Employment → assign-once-never-regenerate, transactional rollback, the dead legacy path |
| EmployeeCode_Security | 8 | Consolidated role×permission matrix, mid-session revocation, composite-FK tenant isolation |
| EmployeeCode_Frontend | 16 | Configuration page, rule builder, manual-code gating, read-only Personal Details banner, missing Vitest coverage |

**Total: 159 test cases, 265 steps, 52 functionalities** (`python qa/tools/build_test_catalogue.py`
regenerates and reports these numbers; treat `coverage-stats.json` as authoritative over this table if they
ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-37**: a legacy `Prefix+NextNumber` Employee Code path in `EmployeeService.cs` has zero callers.
- **CR-38**: no unique constraint/transaction guards the first-ever `EmployeeCodeConfig` row per tenant
  against a concurrent duplicate-create race.
- **CR-39**: Simple-mode padding is capped at 10; a Rule-Based sequence segment's padding is capped at 12.
- **CR-40**: no SQL Server/MySQL provider-acceptance or dedicated concurrency test exists for this feature.
- **CR-41**: an assigned Employee Code is never regenerated on any later employment change.
- **CR-42**: rule-condition hierarchy validation silently no-ops when the parent condition is absent.
- **CR-43**: `EmployeeCodeConditionOperator` has exactly one implemented value (`Equals`).
- **CR-44**: HRAdmin — who performs Initial Employment — holds neither `EmployeeCodeConfiguration.View` nor
  `.Manage`; only SuperAdmin/TenantAdmin can.
- **CR-45**: Simple-mode generated codes have no application-level duplicate guard, unlike Rule-Based.
- **CR-46**: rule request `Conditions`/`Segments` lists are not null-checked before use.

None of these are asserted as confirmed defects — each is exposed by specific test cases and awaits a
product decision, per the classification column in `clarifications.yaml`.
