# Phase 8G — Relieving Letter, Experience Letter & Separation Documents

Phase 8G adds a tenant-scoped, controlled document workflow. It reads the
authoritative separation, clearance, settlement, tenant, and historical
employment records established by Phases 8A–8F. It does not terminate
employment, set final DateOfLeaving, disable accounts, revoke access, or
perform alumni/rehire processing; those are Phase 8H responsibilities.

## Templates and merge fields

Templates are effective-dated masters with immutable draft/published versions.
Only the explicit whitelist below is resolved; unknown fields and executable or
unsafe template content are rejected.

`Employee.FullName`, `Employee.EmployeeCode`, `Employee.DateOfJoining`,
`Employee.Designation`, `Employee.Department`, `Employee.WorkLocation`,
`Separation.ApprovedLastWorkingDate`, `Separation.Reason`, `Separation.Type`,
`Employment.LastDesignation`, `Employment.LastDepartment`, `Employment.LastGrade`,
`Organization.Name`, `Organization.Address`, `Organization.LogoReference`,
`Document.Number`, and `Document.IssueDate`.

The renderer is deliberately narrow: data-only template text is converted to
deterministic PDF bytes. It does not execute C#, Razor, JavaScript, SQL, shell
commands, remote URLs, or local-file references. No digital signature is
claimed; a rendered signature image, if later configured, is not PKI signing.

## Eligibility and historical facts

Official generation requires an approved LWD, an approved/active separation,
completed clearance, finalized Payroll Final Settlement, and an effective
published template. Employment designation, department, grade, and work
location are resolved from the effective employment history at the approved
LWD, not from mutable current values.

Relieving Letter, Experience Letter, Service Certificate, and controlled custom
separation documents use the same authoritative readiness gate in this phase.
The policy can be split later without changing the document model.

## Numbering, snapshots, and integrity

Numbers are tenant/type/year scoped and allocated from a concurrency-token
protected sequence (`RL/2026/000001`, `EL/2026/000001`, etc.). Preview does not
reserve a number. Each official record stores its template/version, merge-data
snapshot, PDF content reference, filename, and SHA-256 content hash.

HR preview renders fixed sample data through the same controlled renderer and
returns a non-official artifact. It does not allocate a number, create a
generated-document row, persist an official artifact, or append lifecycle
events.

Approval validates the stored bytes and never rerenders. Regeneration creates a
new record and number, marks the prior record `Superseded`, preserves its
artifact and snapshot, and requires a reason. History is append-only.

## Storage and visibility

The repository currently has upload metadata but no general blob or PDF
renderer. Phase 8G uses a narrow persisted artifact representation consistent
with the existing tenant database: metadata, a logical storage reference,
immutable snapshot, hash, and encoded PDF bytes. A future blob adapter can move
the bytes without changing the domain contract. Every employee read/download
rechecks tenant ownership, authoritative account-to-employee identity, and
visibility status. Drafts and internal approval details are HR-only.

## Provider, concurrency, and failure boundaries

The four new tables are provider-neutral and receive separate SQL Server and
MySQL migrations. Historical document rows use restrictive relationships. The
service uses EF concurrency tokens, unique tenant document numbers, canonical
current-document detection, and test-only failure injection seams. No public
chaos endpoint or production failure switch exists.

Phase 8G reads Phase 8F settlement readiness and never recalculates Payroll,
gratuity, notice recovery, loans, reimbursements, tax, GL, or net pay. It also
does not mutate employment status or access state.
