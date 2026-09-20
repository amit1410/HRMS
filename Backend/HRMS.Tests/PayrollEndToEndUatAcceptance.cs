using HRMS.Infrastructure.Persistence;

namespace HRMS.Tests;

internal static class PayrollEndToEndUatAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestSupport.TestTenantContext tenant)
    {
        // These existing provider-neutral acceptance paths are composed deliberately:
        // each owns its isolated tenant fixture and verifies the persisted hand-off
        // between payroll calculation outputs and the downstream payroll aggregates.
        await PayrollOutputProviderAcceptance.RunAsync(db, tenant);
        await BankAdviceProviderAcceptance.RunAsync(db, tenant);
        await PayrollAccountingProviderAcceptance.RunAsync(db, tenant);
        await PayrollStatutoryComplianceProviderAcceptance.RunAsync(db, tenant);
        await PayrollRetroSettlementProviderAcceptance.RunAsync(db, tenant);
    }
}
