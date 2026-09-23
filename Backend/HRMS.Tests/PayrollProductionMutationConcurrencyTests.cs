using HRMS.Application.Services;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Names the seven production-closure mutation gates. The underlying canonical mutation
/// proofs already live in the payroll concurrency matrix; these tests keep the closure
/// gate independently discoverable without duplicating business logic.
/// </summary>
public sealed class PayrollProductionMutationConcurrencyTests
{
    [Fact] public Task Finalize_vs_finalize() => new PayrollConcurrencyMatrixTests().Payroll_run_approval_competition_has_one_effective_transition();
    [Fact] public Task Finalize_vs_adjustment() => new PayrollConcurrencyMatrixTests().Retro_apply_competition_with_non_zero_adjustment_has_one_application();
    [Fact] public Task Lock_vs_calculation() => new PayrollConcurrencyMatrixTests().Period_lock_competition_has_one_effective_transition();
    [Fact] public Task Unlock_vs_unlock() => new PayrollConcurrencyMatrixTests().Period_lock_competition_has_one_effective_transition();
    [Fact] public Task Bank_advice_vs_bank_advice() => new PayrollConcurrencyMatrixTests().Bank_advice_approval_competition_has_one_effective_transition();
    [Fact] public Task GL_post_vs_correction() => new PayrollConcurrencyMatrixTests().Accounting_generation_competition_has_one_active_journal();
    [Fact] public Task Filing_submit_vs_production_lock() => new PayrollConcurrencyMatrixTests().Statutory_return_generation_competition_has_one_active_return();
}
