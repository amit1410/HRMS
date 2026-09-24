# Phase 8D — Clearance / No-Dues & Asset Return

## Scope

Phase 8D adds tenant-scoped operational clearance after an approved separation. HR can configure effective-dated templates, start one clearance case for an approved separation, snapshot its items into tasks, resolve or block tasks, capture asset-return metadata, complete the clearance gate, and reopen a completed clearance for controlled rework.

The implementation deliberately does not terminate employment, disable accounts, calculate recovery money, invoke Final Settlement, create payroll artifacts, or implement Exit Interview.

## Model and ownership

`SeparationClearanceTemplate` and `SeparationClearanceTemplateItem` are configurable tenant data. Starting a clearance snapshots active template items into `SeparationClearanceTask` rows, so later template edits do not rewrite historical work. `OwnerType` records the assignment semantics; task authorization remains server-side and tenant-scoped.

`SeparationClearance` is unique per separation. `SeparationClearanceEvent` is append-only. `SeparationAssetReturn` is a narrow operational association for return status, condition, and recovery reference; it does not calculate or post a recovery amount and does not duplicate an enterprise inventory system.

## Lifecycle

An approved or notice-period separation may be started once. The clearance case is `InProgress`. Tasks are `Pending`, `InProgress`, `Cleared`, `Blocked`, `Waived`, or `NotApplicable`. Mandatory tasks must be resolved before completion. Completion changes the separation to `ReadyForExit`; it does not set `Exited`. A completed clearance can be reopened with a reason, preserving all prior history.

Required comments/reasons and required asset returns are validated server-side. Due dates are derived from the template offset and approved LWD when configured. Overdue is derived, not persisted.

## Boundaries and integrations

The service reads approved LWD and notice state from Phase 8C. It does not recalculate notice values. Future LWD changes must be handled by a later schedule-recalculation integration; no cleared task is silently deleted or recreated.

Clearance creates no `FinalSettlementCase`, `PayrollAdjustment`, `PayrollResult`, `BankAdvice`, GL journal, gratuity calculation, loan settlement, reimbursement settlement, or monetary asset recovery. It is an operational readiness gate for a later exit/final-settlement phase.

Employee visibility is read-only and exposes task names, status, due dates, and safe comments. Internal HR/security notes and secrets are not returned. API actions are permission-gated and all rows are tenant-scoped through EF filters and composite tenant relationships.

## Provider and safety

SQL Server and MySQL migrations add the clearance templates, template items, clearances, tasks, events, and asset-return tables with restrictive historical relationships, tenant indexes, and concurrency tokens. Provider wrappers are discoverable through `SeparationClearanceProviderAcceptance`.

The focused SQLite coverage proves template snapshotting, mandatory blocker enforcement, required asset return, completion/`ReadyForExit`, reopen history, and payroll isolation. The deterministic failure-injection suite is discoverable as `SeparationClearanceFailureInjectionTests` and passes 3/3 for task-clear, completion, and asset-return rollback/retry. The six-case `SeparationClearanceConcurrencyTests` suite is also green at 6/6, including canonical loser behavior for clear-vs-block, clear-vs-waive, and asset-return-vs-lost. No production failure switch or public debug hook is used.

MySQL provider acceptance is discoverable and passed first/repeat locally (1/1 and 1/1). SQL Server wrapper discovery is present, but SQL Server acceptance remains an operator gate because `HRMS_SQLSERVER_TEST_CONNECTION` is not configured in this environment. Local model/build checks were run; no migration files from earlier phases were rewritten.

## Deferred

Exit Interview, employment termination, DateOfLeaving, account disablement, Final Settlement, gratuity invocation, notice recovery money, asset monetary deduction, relieving/experience letters, alumni access, and rehire remain deferred.
