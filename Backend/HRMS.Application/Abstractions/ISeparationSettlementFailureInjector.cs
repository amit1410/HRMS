namespace HRMS.Application.Abstractions;

/// <summary>Internal test seam only. Production composition leaves this null.</summary>
public interface ISeparationSettlementFailureInjector
{
    void BeforeInitiationCommit();
    void BeforePayrollLinkCommit();
    void BeforeCompletionSyncCommit();
}
