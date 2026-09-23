# Payroll production support runbook

## First response

Capture tenant, payroll period/run ID, UTC time, correlation ID, operation, provider, and the safe error code. Do not request passwords, connection strings, bank account numbers, tax documents, or raw filing payloads.

Check liveness/readiness, production health, integrity, current locks, run history, and the relevant audit trail. A configuration, authorization, concurrency, provider, external-integration, data-integrity, retryable-infrastructure, and permanent-infrastructure failure must be handled differently.

## Retry guidance

Retry only operations whose existing service contract is retry-safe and only after checking durable state. Do not retry finalization, bank export, GL posting, or external filing blindly after an unknown response; verify the durable record and external reference first. Concurrency conflicts should be reloaded and intentionally retried by an authorized operator.

## Escalation

Escalate unresolved integrity findings, contradictory finalized states, duplicate monetary effects, provider outages, migration failures, or any suspected tenant-isolation issue. Preserve evidence and do not edit finalized artifacts directly.
