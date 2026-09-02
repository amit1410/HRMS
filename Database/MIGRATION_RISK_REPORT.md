# Populated-Database Migration Risk Report

**Scope:** This review identifies the two unsafe migrations requested for separate approval. No migration was executed, edited, generated, or applied; no production data or schema was changed.

## 1. `20260827035148_AddEmploymentDetailsAndPositionHistory`

- **Why unsafe:** `Up` drops the existing `EmployeeEmploymentHistory` string values before creating replacement foreign-key fields. It contains no data-preserving mapping/backfill. Existing history can therefore lose its department, function, grade, company, organisation, location, section, sub-department, sub-function, sub-section, and work-location meaning. New `EmployeeEmployments`, `EmployeeEmploymentHistory`, and hierarchy master tables/FKs also impose new relationships on populated data.
- **Tables/columns affected:** `EmployeeEmploymentHistory` (the string columns above are dropped; new `CostCenterId`, `CountryLocationId`, `EmployeeTypeId`, `FunctionId`, `GradeId`, `HoldingCompanyId`, `LobId`, `OrganisationId`, `PositionChangeReasonId`, `SectionId`, `SubDepartmentId`, `SubFunctionId`, `SubSectionId`, `WorkLocationId` are added); `Employees` (`CostCenterId`, `EmployeeTypeId`); newly created employment and hierarchy tables including `EmployeeEmployments`, `CostCenters`, `EmployeeTypes`, `Functions`, `Grades`, `HoldingCompanies`, `Organisations`, `PositionChangeReasons`, `SubDepartments`, `WorkLocations`, `SubFunctions`, `LinesOfBusiness`, and related tables in the migration.
- **Existing-data risk:** historical display values are irreversibly lost; rows may become unmappable/orphaned, hierarchy combinations may be invalid, and tenant-scoped IDs can be assigned incorrectly.
- **Required backfill/validation:** inventory and snapshot old values; resolve each value to a tenant-scoped master by normalized code/name; quarantine unresolved/ambiguous rows; populate snapshot fields and new FKs; verify row counts, tenant agreement, non-null/orphan-free FKs, valid hierarchy relationships, and effective-date history before constraints are enforced.
- **Rollback:** the generated `Down` is not data-safe because it cannot restore dropped values. Require a full backup/PITR and restore-based rollback; do not rely on `Down` for recovery.

## 2. `20260830061620_AddBankMasterAndEmployeeBankRefinements`

- **Why unsafe:** `Up` drops `EmployeeBankDetails.BankName` first, then adds non-null `BankId` with `Guid.Empty`, `Status` default `0`, and `IsActive` default `true`, and finally creates a tenant-scoped FK. There is no mapping from existing names to `Banks`; the FK can fail on existing rows and bank names are lost.
- **Tables/columns affected:** `EmployeeBankDetails` (`BankName` dropped; `BankId`, `BranchName`, `EffectiveFrom`, `IsActive`, `Status` added); new tenant-scoped `Banks` table, indexes, and `FK_EmployeeBankDetails_Banks_TenantId_BankId`.
- **Existing-data risk:** irreversible bank-name loss, `Guid.Empty` FK violations, incorrect status/active state, and accidental reactivation of historical accounts.
- **Required backfill/validation:** create/review tenant-scoped bank masters from distinct legacy names; map every row before dropping the legacy column; explicitly map legacy status and active state; validate uniqueness, tenant matching, non-null/orphan-free FKs, and current-vs-historical lifecycle semantics.
- **Rollback:** `Down` drops the new data and re-adds an empty `BankName`; it cannot reconstruct names. Use backup/PITR restoration, or a staged compatibility rollback while the legacy column is retained.

## Recommended safe sequence

1. Take a verified backup and capture row counts, hashes, and tenant-level inventories.
2. Use an approved **additive** migration: retain legacy columns, add nullable/staging columns and new tables/FKs without enforcement.
3. Backfill and reconcile employment hierarchy and bank masters; record exceptions for manual resolution.
4. Validate mappings, tenant isolation, snapshots, statuses, effective dates, and orphan checks; perform a dry-run constraint check.
5. Run a compatibility period with dual-read/controlled writes, then use a later approved migration to enforce non-null/unique constraints and drop legacy columns.

Direct application requires a maintenance window for locks, table/index work, and possible failure recovery. A staged approach can keep additive/backfill work online, with only a brief final constraint/drop window; duration depends on populated row counts and index size.

