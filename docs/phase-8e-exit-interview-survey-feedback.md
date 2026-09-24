# Phase 8E — Exit Interview, Exit Survey & Employee Exit Feedback

Phase 8E adds a separation-scoped exit interview workflow without changing employment termination, final settlement, payroll, documents, or account status.

## Lifecycle

HR configures a tenant-scoped template and creates an immutable draft version. A published version is effective-dated and cannot overlap another published version for the same template. At assignment time, the effective published version is resolved using the assignment business date and stored on the interview. The version reference preserves questionnaire semantics for the assigned interview.

The employee can save a draft and submit once all required employee-visible questions are valid. Submission changes the interview to `EmployeeSubmitted`. HR can start the interview, add confidential notes, record reason-for-leaving feedback and a non-authoritative rehire recommendation, and complete the interview. Completion without employee participation requires an explicit reason. A privileged HR user can reopen a completed interview with a reason.

## Data and confidentiality

Templates, versions, questions, options, interviews, responses, response revisions, HR notes, and events are tenant-scoped. Employee DTOs do not contain HR notes, HR observations, or rehire recommendations. HR notes are stored separately and are authorized by the HR API boundary; they are not hidden only in the browser. Response revisions and interview events are append-only records.

Question types are constrained to choice, multi-choice, rating, yes/no, short/long text, number, and date. Validation is performed server-side against configured metadata.

`PrimaryReasonCategory` and related feedback fields are employee feedback, not a replacement for the operational separation reason. `RehireRecommendation` is input only and does not create or decide future rehire eligibility.

## Authorization and tenancy

Employee self-service uses the authoritative account-to-employee identity resolver and never accepts an arbitrary employee id. HR operations and configuration use existing separation HR/configuration permissions. Every query is tenant filtered and composite tenant foreign keys prevent cross-tenant references.

## Concurrency and failure boundaries

Interviews and responses use concurrency versions. State transitions and response revisions are saved atomically; a concurrency conflict returns a retryable conflict and does not create a second authoritative transition. Historical events are never updated or deleted through this feature.

## Provider and scale foundation

SQL Server and MySQL migrations are generated from the same model. Inbox reads are paged server-side. The model supports bounded large-data acceptance for interviews and responses, and future aggregate analytics can query status, reason categories, ratings, and recommendation distributions without exposing confidential free text.

## Relationship to Phase 8D

Exit interview assignment is independent of clearance and ReadyForExit. It may run in parallel with clearance and does not mark an employee exited.

## Explicit boundaries and deferred scope

Phase 8E does not set DateOfLeaving, terminate employment, deactivate accounts, calculate or finalize payroll, create settlement or benefits entries, create monetary recovery, generate relieving/experience letters, or create alumni/rehire workflow. Final settlement orchestration and separation closure belong to Phase 8F and must reuse the existing payroll settlement and separation-benefits capabilities.
