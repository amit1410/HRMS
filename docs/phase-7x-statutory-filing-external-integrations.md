# Phase 7X — Statutory Filing & External Integrations

Status: FINAL OPERATOR REVERIFICATION PASSED.

## Scope

Phase 7X adds a tenant-scoped filing orchestration layer over finalized statutory register data. It does not calculate payroll or statutory liability, rewrite finalized payroll, or claim an official government filing integration. The initial production destination is `ManualDownload`; an operator generates and downloads an immutable package, uploads it to the external portal, and records the acknowledgement.

The layer includes filing definitions and versions, safe field-mapping metadata, filing runs, source-row snapshots, validation issues, immutable CSV packages, SHA-256 integrity hashes, submissions and attempts, manual acknowledgements, lifecycle history, tenant filters, permission-gated APIs, maker-checker approval, connector profiles, and connector resolution through `IStatutoryFilingConnector`/`StatutoryFilingConnectorRegistry`. ManualDownload is the supported operator workflow; a deterministic Test connector is available for lifecycle/provider tests. No official government portal integration is claimed.

## Source and lifecycle rules

Source rows are resolved from persisted, non-draft/non-cancelled Phase 7K statutory return employees for the requested filing period. Draft or staged payroll data is not used. Packages are immutable once submitted; regeneration creates a new package version. A unique tenant/run/package-hash index prevents duplicate content within a filing run while allowing distinct runs to generate identical valid content. Rejected/failed resubmission creates a new package and submission with links to the prior package/submission; the prior evidence remains immutable. Concurrent generation treats a database uniqueness race as a safe replay of the committed package.

The lifecycle is Draft → Generated → Validated → SubmittedForApproval → Approved → Submitted → Acknowledged, with Cancelled available only before submission. Invalid transitions return conflicts. Submission stores a safe response summary and never stores portal credentials or raw external payloads.

## Security and non-goals

All entities are tenant scoped and protected by global query filters and composite foreign keys. Connection profiles persist only non-secret configuration and an optional secret reference; raw credentials are not stored or returned. Package download is permission gated and returns controlled content rather than server paths. No generic request-idempotency middleware, arbitrary formulas/scripts, OCR, government portal automation, SFTP, webhook callbacks, or official statutory certification is claimed in this phase.

## Evidence completed so far

- API/service/domain/configuration compile successfully; infrastructure, API, and solution builds pass.
- SQL Server and MySQL Phase 7X migrations were scaffolded from the same provider-neutral model; model snapshots were synchronized when generated.
- SQLite provider-neutral foundation and connector focused tests: 3/3 PASS.
- Focused Phase 7X after connector/profile/lineage changes: 19/19 PASS (foundation 1, connector registry 2, concurrency 7, retry-safety 8, large-data 1).
- Concurrency suite: 7 independently named tests; 7/7 PASS.
- Retry-safety suite: 8 independently named tests; 8/8 PASS.
- Large-data source/package test: 10,000 employees/source rows; 1/1 PASS in 12 seconds.
- MySQL provider acceptance: first 1/1 PASS in 37 seconds; repeat 1/1 PASS in 36 seconds.
- Payroll fast regression: 138/138 PASS.
- Frontend regression: 609/609 PASS; production build PASS; TypeScript PASS; lint PASS with existing warnings.
- `git diff --check`: PASS.
- A seed-ID collision found by the backend regression was corrected by assigning StatutoryFiling permissions IDs 223–232, after the existing YearEndTax IDs 215–222.
- The frozen non-provider discovery filter (`FullyQualifiedName!~SqlServer & FullyQualifiedName!~MySql & FullyQualifiedName!~ProviderAcceptance`) now discovers 1,288 tests. Parenthesized class groups with the same exclusions were executed sequentially and exactly accounted: 1,288 passed, 0 failed, 0 skipped, 0 blocked, 0 unaccounted. The arithmetic is 1,288 = 1,288 + 0 + 0 + 0.
- The SQL Server wrapper is discoverable as `SqlServer_statutory_filing_provider_acceptance`, uses `StatutoryFilingProviderAcceptance.RunAsync`, and has only the canonical environment-based skip when `HRMS_SQLSERVER_TEST_CONNECTION` is absent.
- Supplied operator evidence: SQL Server first 1/1 in 10.9444 seconds; repeat 1/1 in 8.3693 seconds with `--no-build`; the SQL Server design-time pending-model check with `Database__Provider=SqlServer` reported no changes since the last migration.
- Connector registry focused tests: 2/2 PASS.
- Shared provider acceptance now proves a tenant-scoped Test connector profile, secret-reference redaction, deterministic rejection, immutable original package/submission, corrected package generation, new accepted submission, and resubmission lineage.
- MySQL connector/profile/lineage acceptance after the model increment: first 1/1 PASS in 44 seconds; repeat 1/1 PASS in 45 seconds.
- New current Phase 7X migration increments: connector profiles/lineage and package-hash uniqueness adjustment for SQL Server and MySQL. Phase 7A–7W committed migrations remain unchanged; SQL Server pending-model and provider evidence must be rerun by the operator.

## Closure

Final operator verification is complete: SQL Server provider first/repeat passed on the same database, and the SQL Server design-time pending-model check with `Database__Provider=SqlServer` returned no changes since the last migration. Final status: `PHASE 7X STATUTORY FILING & EXTERNAL INTEGRATIONS: COMPLETE`. Any earlier deferred-verification wording below is historical and superseded by this closure result.

SQL Server provider acceptance and SQL Server pending-model verification are required again because the current uncommitted Phase 7X model/migration increment changed connector/profile and package-lineage persistence. SQL Server was not executed in this pass. Final status: `PHASE 7X IMPLEMENTATION COMPLETE — SQL SERVER OPERATOR REVERIFICATION REQUIRED` only after the non-SQL evidence remains green and the operator reruns SQL Server first/repeat plus the pending-model check.
