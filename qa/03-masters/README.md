# Module 3 — Masters / Organization Structure

Status: manual test-case catalogue complete and validated. No automation implemented yet (Phase 8+, on
explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (40 functionalities: F-MST-01…40). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-generic-master-listing.yaml` … `cases/10-master-frontend.yaml` | The manual test cases, one YAML file per Excel worksheet (`sheet:` key), one case per `id`. |
| `clarifications.yaml` | The CR-28…CR-36 QA-risk register for this module (numbering continues after Module 2's CR-27). |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here.

## The real master hierarchy (source-verified, not assumed)

Discovered by reading `MasterManagementController`/`Service`, `MasterLookupController`/`Service`,
`MasterImportController`/`Service`, `DepartmentsController`/`DesignationsController`/`CountriesController`/
`StatesController`/`CitiesController` and their services, every relevant entity/EF configuration, and the
frontend `MasterManagementPage.tsx`/`MasterDropdown.tsx`. There are **three separate engines**, not one:

1. **Generic engine** (`api/masters/{kind}`, permission `Geography.View`/`Geography.Manage`) — 15 tenant-owned
   kinds sharing one CRUD implementation (`MasterManagementService`): `holding-companies`,
   `lines-of-business`, `organisations`, `departments`\*, `sub-departments`, `sections`, `sub-sections`,
   `functions`, `sub-functions`, `grades`, `designations`\*, `employee-types`, `work-locations`,
   `cost-centers`, `position-change-reasons`. `countries` is registered for **read only** — every write is
   rejected with 409 before it reaches the per-kind logic (`CountriesController` is the real write path).
   \* `departments`/`designations` are *also* reachable here even though they have their own dedicated
   controllers below — see CR-29.
2. **Dedicated controllers** (own permission, own validator, own service): `departments` (`Department.*`),
   `designations` (`Designation.*`), `countries`/`states`/`cities` (`Geography.*`, **global, not
   tenant-scoped**, hard-deleted rather than soft-deactivated).
3. **Lookup-only** (`api/master-data/{kind}`, `MasterLookupController`): the same 15 generic kinds (permission
   `Department.View`, except `designations`=`Designation.View` and `position-change-reasons`=
   `EmploymentHistory.View`) **plus `banks`** — Bank has no create/update/delete surface anywhere in the
   product (CR-31).

**Bulk import** (`api/master-import/{kind}`) covers only the 15 generic-engine kinds — never
countries/states/cities/banks.

**Hierarchy actually implemented:**

```
Holding Company --[optional]--> Line of Business
Department --[REQUIRED]--> Sub Department --[optional]--> Section --[optional]--> Sub Section
Function --[optional]--> Sub Function
Country --[REQUIRED]--> State --[REQUIRED]--> City   (global, not tenant-owned)
```

Organisation, Grade, Designation, Employee Type, Work Location, Cost Center, Position Change Reason and Bank
are **flat** — no parent concept at all, regardless of where they sit in the UI's grouping.

## Coverage matrix — which generic behavior applies to which master kind

Per the brief's instruction not to duplicate identical CRUD cases per kind: the tables below show which
kinds were used as the **exemplar** for each generic behavior (fully tested), which were used for a
**coverage-matrix spot check** (one or two calls confirming the same code path), and which have **kind-specific**
cases beyond the generic contract.

| Behavior | Fully exercised on | Spot-checked on | Kind-specific cases |
|---|---|---|---|
| List/Get (paging, search, active filter, no sort) | holding-companies | grades, cost-centers, countries | — |
| Create (validation, charset, uniqueness) | holding-companies | grades (SortOrder ignored) | Department/Designation (own validator, own charset rule — CR-29) |
| Update (full replace, uniqueness, parent) | holding-companies, lines-of-business, sub-departments | — | Department/Designation |
| Delete (reference-checked deactivate) | holding-companies, functions | cost-centers (repeat cycle) | Department/Designation (own guard); departments/designations via the generic route (CR-28 gap) |
| Parent required/optional/invalid/inactive/cross-tenant | sub-departments (required), lines-of-business (optional) | sections, sub-sections, sub-functions | — |
| Child-reference check on delete | holding-companies, functions (present) | sub-departments, sections (**absent** — CR-35) | — |
| Country/State/City CRUD | all three, in full | — | hard-delete contract, global sharing |
| Lookup (dropdown) endpoints | holding-companies | organisations, work-locations, cost-centers, grades (SortOrder) | designations (own permission), position-change-reasons (own permission), banks (read-only-only) |
| Import (template/validate/confirm) | cost-centers, sub-departments | sections (second hierarchical kind) | — |
| Authorization matrix | holding-companies, departments, designations | grades, countries | — |
| Tenant isolation | holding-companies, sub-departments (parent injection) | departments, designations, grades | Country/State/City (deliberately shared, not isolated) |
| Frontend (MasterManagementPage/MasterDropdown/MasterBulkImport) | holding-companies, countries, departments | — | states/cities/banks (absent — CR-32) |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| Masters_Generic_List | 18 | Generic engine List/Get: paging, search, active filter, clamp-not-reject boundaries, no client sort, cross-tenant/malformed id |
| Masters_Generic_Create | 31 | Generic engine Create validation (code charset/length, name length vs CR-33, uniqueness, parent rules) plus dedicated Department/Designation create |
| Masters_Generic_Update | 21 | Generic engine Update (full-replace semantics, uniqueness, parent re-validation, no concurrency control) plus dedicated Department/Designation update |
| Masters_Status_Delete | 25 | Reference-checked soft deactivation, the Department/Designation generic-route gap (CR-28), idempotent repeat delete, dedicated Department/Designation delete |
| Masters_Hierarchy | 15 | Parent/child cascading masters end to end, the child-reference-check asymmetry (CR-35), stale-parent lock-out, deep chains, flat-kind confirmation |
| Masters_Location | 30 | Country/State/City CRUD, the intentional hard-delete-vs-soft-deactivate contrast, global tenant sharing, the frontend write-path gap (CR-32) |
| Masters_Lookups | 16 | All 15 generic lookup kinds plus Banks, the lookup-vs-CRUD permission divergence (CR-30), SortOrder-driven ordering, search case-sensitivity (CR-36) |
| Masters_Import | 24 | Template/validate/confirm, mixed valid/invalid rows, CreateOnly vs CreateOrUpdate, atomic all-or-nothing confirm, unsupported-kind refusal |
| Masters_AuthZ_Security | 12 | Consolidated role×permission matrix, cross-tenant probing sweep, the Geography.Manage bypass of Department/Designation permissions (CR-29) |
| Masters_Frontend | 14 | MasterManagementPage permission gating and read-only Countries, MasterDropdown cascading/capping, bulk-import gating, the missing States/Cities/Banks page (CR-32) |

**Total: 210 test cases, 330 steps, 40 functionalities** (`python qa/tools/build_test_catalogue.py`
regenerates and reports these numbers; treat `coverage-stats.json` as authoritative over this table if they
ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-28 / CR-35**: the generic engine's delete-reference-check is inconsistent — present and correct for
  Holding Company→LOB and Function→SubFunction, entirely absent for `departments`/`designations`, and present
  but incomplete (employment-only, no child-master check) for Sub Department→Section and Section→SubSection.
- **CR-29**: Department and Designation are each writable through two independent permission models
  (`Department.*`/`Designation.*` via their dedicated controllers, or `Geography.Manage` via the generic
  route) that also apply different validation and different delete safety.
- **CR-30 / CR-34**: the lookup endpoints and the CRUD endpoints for the same 15 kinds are gated by different,
  seemingly-unrelated permissions, and the CRUD permission (`Geography.*`) covers far more than geography.
- **CR-31**: Bank is fully modeled but has no write API anywhere.
- **CR-32**: Country/State/City have complete backend CRUD but zero frontend write path.
- **CR-33**: the generic engine's Name validation (200 chars) does not match its own tables' column length
  (100 chars).
- **CR-36**: lookup search is not case-normalized the way the generic list search is.

None of these are asserted as confirmed defects — each is exposed by specific test cases and awaits a
product decision, per the classification column in `clarifications.yaml`.
