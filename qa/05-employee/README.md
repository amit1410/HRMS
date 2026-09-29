# Module 5 — Employee Management

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py`
regenerates `qa/HRMS_Test_Cases.xlsx` across all five modules with no errors). No automation implemented yet
(Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (170 functionalities: F-EMP-001…170). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-employee-list.yaml` … `cases/14-other-subresources.yaml` | The manual test cases, one YAML file per topic area, several files sharing a `sheet:` (Excel worksheet) — see the table below. |
| `clarifications.yaml` | The CR-47…CR-65 QA-risk register for this module, plus short cross-references to CR-28/CR-29 (Module 3) and CR-44/CR-45/CR-46 (Module 4) that this module's cases exercise directly. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here.

## The real implementation (source-verified, not assumed from `docs/` or the old requirements)

Discovered by reading `EmployeesController`, `EmployeeSubResourcesController`, `MeController`,
`EmployeeService`, `EmployeeEmploymentService`, `EmployeeSupervisorService`, `EmployeeManagerResolver`,
`EmployeeAccessScopeService`, `EmployeeContactService`, `EmployeeAddressService`,
`EmployeeBankDetailService`, `EmployeeFamilyService`, `EmployeeEducationService`,
`EmployeePreviousEmploymentService`, `EmployeeAdditionalInfoService`, `EmployeeDocumentService`,
`EmployeeAuditService`, `EmployeePortalAccountService`, every `Employee*RequestValidator`, every
`Employee*` entity and EF configuration under `Backend/HRMS.Infrastructure/Persistence/Configurations`,
`Domain/Authorization/Permissions.cs`, `SeedData.RolePermissionMap`, both migration chains, and the
frontend `EmployeesPage.tsx`, `EmployeeFormPage.tsx`, `EmployeeDetailPage.tsx`,
`EmployeeProfileSidebar.tsx`, `PersonalDetailsForm.tsx`, `ContactDetailsForm.tsx`,
`AddressDetailsForm.tsx`, `BankDetailsForm.tsx`, `EmploymentSectionForm.tsx`,
`SupervisorSectionForm.tsx`, `FamilyDetailsForm.tsx`, `PreviousEmploymentForm.tsx` and
`PortalAccessCard.tsx`.

**Eleven sub-resources exist** under `/api/employees/{id}`: Contact (1:1), Addresses (Current/Permanent,
one row per type), Family (unbounded), Education (unbounded), Previous Employment (unbounded, with linked
Documents), Bank Details (unbounded, app-level one-active-per-purpose), Supervisor (L1-L5/Time/ERO/CHRO,
1:1), Additional Info (1:1), Employment (joining/contractual, 1:1) + Employment History (effective-dated,
append-only), Documents, and an Audit Log. **Eight have a frontend tab** — Personal, Contact, Address,
Family, Bank, Previous Employment, Employment, Supervisor, all present in both `EmployeeFormPage.tsx`'s and
`EmployeeDetailPage.tsx`'s `TABS` list. Education, Additional Info, Documents and the Audit Log have **no
frontend surface at all** — their `api/employeeSubsections.ts` functions are called nowhere outside that
module (CR-52).

**Employee creation has two write paths.** The legacy full `EmployeeRequest` (`POST`/`PUT /api/employees`)
requires a client-supplied Employee Code and accepts department/designation/manager/email immediately,
gated by `Employee.Create`/`.Edit` **and** `EmployeeSensitive.Edit`. The modern Personal-Details-only path
(`POST`/`PUT /api/employees/personal-details`) needs only `Employee.Create`/`.Edit`, leaves `EmployeeCode`
null, and assigns a uniqueness-safe placeholder email (`emp-{guid}@placeholder.local`, stripped back to
null on read). The two paths disagree on whether `EmployeeSensitive.Edit` is a hard gate: the full path
403s without it; the Personal-Details path silently drops the sensitive fields instead (CR-62).

**Employment History is the single append-only, effective-dated source of truth** for Department,
Designation, Grade, Manager and nine other organizational FKs. `Employee`'s own
`DepartmentId`/`DesignationId`/`Status`/`ReportingManagerId`/`EmployeeTypeId`/`CostCenterId` columns are a
denormalized snapshot, synced only when a new history record's effective date is today or earlier — never
for a future-dated record, and the **List and Export endpoints read two different sources for the same
filter** (List is history-aware with a snapshot fallback; Export always reads the legacy snapshot, CR-47).
**No effective date may ever be in the past**, for any `ChangeReason` including `Correction` — a
backdated correction has no supported path through this API (CR-51).

**The direct (L1) manager is resolved exclusively from current Employment History** via
`EmployeeManagerResolver`; `EmployeeSupervisor.L1ManagerId` is a read-only compatibility mirror the
Employment engine keeps in sync, and the Supervisor endpoint refuses to let a caller set L1 to anything
Employment does not already resolve — including refusing the **entire** request (even an unrelated
ERO/Time-Manager edit) when a `LegacyConflict` exists (CR-54). L2-L5/Time/ERO/CHRO are free peer fields
with only a not-self/must-be-active-in-tenant check; `SupervisorType` has no `L4`/`L5`/`Ero`/`Chro`
members, so those four DTO-exposed slots have **no** eligible-candidate dropdown source at all (CR-59).

**The single highest-impact finding in this module (CR-53):** neither the legacy full `PUT` nor a plain
`EmploymentStatus=Terminated`/`Resigned` position-change ever touches `User.IsActive` — only the sanctioned
`SeparationExitService` does. An HR user holding only `Employee.Edit` can terminate someone and their portal
login stays fully active indefinitely, and there is no API to revoke an already-active account outside
Separation (`RevokeAsync` only accepts a still-pending invitation).

**Delete is a hard delete** (row removal), documented as being for correcting a mistake, not off-boarding —
guarded only against the legacy `Employee.ReportingManagerId` FK. Confirmed directly from
`EmployeeEmploymentHistoryConfiguration.cs`: the history table's own `ManagerId` FK is
`OnDelete(DeleteBehavior.SetNull)`, not `Restrict` — a manager assigned only through Employment History can
be deleted with no warning, silently nulling every historical record that named them (CR-56). **No
optimistic-concurrency token exists anywhere in the Employee entity family** (confirmed by grep across
every `Employee*Configuration.cs` — only Module 4's `EmployeeCodeSequence` has one), so two concurrent
writes are last-write-wins with no 409/412 (CR-58).

**Row-level scoping** (`EmployeeAccessScopeService`) narrows `GetAsync`/`GetByIdAsync` to a role's
department/section/grade assignment, or — via a cross-module coupling to the *Attendance*
`MonthlyViewTeam` permission, not any Employee-module permission — narrows the seeded `Manager` role to
self+direct-reports only (CR-57). **Export and the raw sensitive-details read apply no scope predicate at
all** (CR-48), so a scope-restricted role can export or read-raw the whole tenant. Sub-resource
authorization is a **three-axis** decision, not two: `EmployeeScopeAuthorizationFilter` returns 404 — not
403 — for an out-of-scope-but-same-tenant employee, even when the caller holds the exact declared
permission (CR-55).

## Coverage matrix — which behavior is exercised where

| Behavior | Fully exercised on | Spot-checked on | Kind-specific cases |
|---|---|---|---|
| List/search/filter/sort | paging bounds, 4 filters combined, 9-field sort whitelist | history-vs-legacy sort divergence, Manager self-scope | reference-filter permission-gating (frontend) |
| Create (both paths) | full-record + Personal-Details, duplicate/cross-tenant/inactive references | sensitive-field silent-drop asymmetry, birth-location cascade | uniqueness-race → handled Conflict |
| Personal Details | every field's exact bound (14yr age floor, Aadhaar/PAN/UAN regex, ESIC conditional) | masked-vs-raw read, scope-bypass on sensitive-details | Unicode round-trip |
| Contact / Address | upsert-in-place semantics, official-email uniqueness, hard address delete | placeholder-email leak guard, dead "same as current" flag | concurrent double-create race |
| Bank Details | one-active-per-purpose, Active-only-on-create, one-way historical transition | masked list vs. unmasked edit-only endpoint, inactive-bank reassignment rule | idempotent soft-delete |
| Employment / History | full hierarchy+FK validation, no-past-dating, same-day Correction/revision, gap/overlap reconciliation | snapshot-frozen-at-write-time, manager-carry-forward exception | Rule-Based master-code resolution timing |
| Manager / Supervisor | L1-is-Employment-controlled, self/direct/indirect cycle prevention, date-aware resolution | LegacyConflict blocks unrelated edits, no L4/L5/ERO/CHRO options source | manager-role auto-provisioning |
| Status / Lifecycle | 3-member enum, DateOfLeaving pairing | **terminated-employee-keeps-login (CR-53), no revoke-active-account API** | rehire with no transition guard |
| Update / Delete / Export | cycle/uniqueness/reference re-validation, direct-reports delete guard | **ManagerId FK is SetNull not Restrict (CR-56)**, export/list dual-source divergence (CR-47) | CSV formula-injection neutralization, 10k row cap |
| Employee Code integration | assign-once-at-Initial-Employment, transactional rollback | HRAdmin can't view the config it consumes (CR-44), Simple-mode collision → 500 (CR-45) | Manual/Rule-Based branch selection |
| Authorization / Tenant Isolation | full role×permission matrix, composite-FK cross-tenant rejection | Export/sensitive-details bypass row scope (CR-48), 404-not-403 scope filter (CR-55) | AccountEmployeeLink.* excluded from SuperAdmin |
| Frontend | 8-tab gating, URL round-trip, debounced search, manager display never a GUID | reference-filter permission-hiding, export-drops-paging | zero Vitest for 4 sub-resources with no UI at all |
| Family/Education/PrevEmployment/AdditionalInfo/Documents/Audit | required-field validation per sub-resource, tenant isolation | nominee-percentage has no aggregate check (CR-60), Audit Log has no validator (CR-49) | audit rows always write EmployeeCode=null (CR-63) |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| Employee_List | 16 | Paging/search/filter/sort matrix, tenant + Manager-scope isolation |
| Employee_Core | 38 | Create (both paths), Update, Delete, Export, Import — the Employee entity's own CRUD lifecycle |
| Employee_Personal | 16 | Every Personal Details field's exact validation bound, masked/raw sensitive-data split |
| Employee_Address | 15 | Contact upsert + official-email uniqueness, Address CRUD, the dead "same as current" flag |
| Employee_Bank | 14 | One-active-per-purpose, Active-only-create, one-way historical transition, masked/unmasked split |
| Employee_Employment | 7 | The 1:1 joining/contractual record (probation, notice, referrer validation) |
| Employee_Manager | 19 | L1-is-Employment-controlled, cycle prevention, LegacyConflict, supervisor-options eligibility |
| Employee_History | 18 | Effective-dating engine: no-past-dating, same-day Correction/revision, gap/overlap reconciliation |
| Employee_Lifecycle | 8 | 3-member status enum, and the CR-53 terminated-employee-keeps-login finding in depth |
| Employee_CodeIntegration | 8 | Employee-side impact of Module 4's CR-44/CR-45, assign-once transactional guarantee |
| Employee_Security | 15 | Consolidated permission matrix, tenant-isolation sweep, Audit Log validation gaps |
| Employee_Frontend | 14 | Tab gating, URL state, manager display, permission-hidden filters, export composition |
| Employee_Subresources | 13 | Family/Education/Previous Employment/Additional Info/Documents/Audit Log, and the frontend-coverage gap (CR-52) |

**Total: 201 test cases, 429 steps, 170 functionalities** (`python qa/tools/build_test_catalogue.py`
regenerates and reports these numbers; treat `coverage-stats.json` as authoritative over this table if they
ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-47**: List and Export filter/sort by two different data sources for the same query parameters.
- **CR-48**: Export and the raw sensitive-details read apply no row-level access-scope predicate.
- **CR-49**: `AuditQuery` has no validator; an unbounded pageSize is silently clamped, an unsupported sort
  field is silently ignored, unlike every other paged query in this module.
- **CR-50**: Employee Import is scaffolding only — a batch is created, no file is ever parsed.
- **CR-51**: An employment change's effective date may never be in the past, even under `Correction`.
- **CR-52**: Education, Additional Info, Documents and the Audit Log have zero frontend representation.
- **CR-53**: Terminated/Resigned employees keep an active portal login — the single highest-impact finding.
- **CR-54**: A `LegacyConflict` blocks the entire Supervisor upsert, including unrelated fields.
- **CR-55**: `EmployeeScopeAuthorizationFilter` returns 404, not 403, for an out-of-scope employee.
- **CR-56**: Deleting an employee only guards the legacy manager FK; the history FK is `SetNull`, confirmed.
- **CR-57**: The Manager role's scope narrowing is driven by an Attendance permission, not an Employee one.
- **CR-58**: No optimistic-concurrency token exists anywhere in the Employee entity family.
- **CR-59**: `SupervisorType` has no L4/L5/ERO/CHRO members — no options source for those DTO slots.
- **CR-60**: Family nominee-percentage validation is per-record only, never summed across records.
- **CR-61**: "Same as Current Address" has no server-side enforcement — a client-only save-time mirror.
- **CR-62**: Full-record vs. Personal-Details create/update disagree on whether `EmployeeSensitive.Edit`
  is a hard permission gate.
- **CR-63**: Every sub-resource audit-log row hardcodes `EmployeeCode=null`.
- **CR-64**: No xUnit test file exists for `EmployeeManagerResolver`, `EmployeeFamilyService`,
  `EmployeeEducationService`, `EmployeePreviousEmploymentService`, `EmployeeDocumentService`,
  `EmployeeAdditionalInfoService`, or `EmployeeAddressService`.
- **CR-65**: Address has a DB-level unique index but no application-level concurrent-insert handling.

`clarifications.yaml` also carries short cross-references to **CR-28/CR-29** (Module 3, Masters
delete-safety and the Department/Designation dual-write-path) and **CR-44/CR-45/CR-46** (Module 4, Employee
Code) — each is a pointer back to the originating module's full write-up, re-declared only so a Module 5
case's `cr:` field resolves against this module's own registry.

None of these are asserted as confirmed defects — each is exposed by specific test cases and awaits a
product decision, per the classification column in `clarifications.yaml`.
