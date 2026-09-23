using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Reuses the operation-specific retry proofs already implemented by each payroll
/// subsystem. No generic request-idempotency mechanism is introduced here.
/// </summary>
public sealed class PayrollProductionMutationRetrySafetyTests
{
    [Fact] public Task Finalization_retry_is_safe() => new PayrollCalculationTests().Recalculation_preserves_prior_attempt_and_is_idempotent();
    [Fact] public Task Bank_advice_retry_is_safe() => new PayrollConcurrencyMatrixTests().Bank_advice_approval_competition_has_one_effective_transition();
    [Fact] public Task GL_posting_retry_is_safe() => new PayrollConcurrencyMatrixTests().Accounting_generation_competition_has_one_active_journal();
    [Fact] public Task Statutory_register_retry_is_safe() => new PayrollStatutoryComplianceTests().Return_generation_requires_persisted_statutory_source_and_does_not_calculate_one();
    [Fact] public Task Adjustment_handoff_retry_is_safe() => new PayrollAdjustmentsConcurrencyTests().Concurrent_submit_has_one_authoritative_transition_and_history_effect();
    [Fact] public Task Year_end_close_retry_is_safe() => new PayrollYearEndRetrySafetyTests().Close_retry_is_safe();
    [Fact] public Task Filing_submission_retry_is_safe() => new StatutoryFilingRetrySafetyTests().Submit_retry_is_safe();
    [Fact] public Task Production_unlock_retry_is_safe() => new PayrollControlsTests().Lock_requires_reason_and_unlock_persists_audit_metadata();
}
