namespace HRMS.Domain.Enums;

public enum SeparationSettlementOrchestrationStatus
{
    NotReady,
    Ready,
    InitiationPendingApproval,
    Initiated,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public enum SeparationSettlementEventType
{
    SettlementReadinessEvaluated,
    FinalSettlementInitiationRequested,
    FinalSettlementLinked,
    FinalSettlementProcessing,
    FinalSettlementFailed,
    FinalSettlementRetryRequested,
    FinalSettlementCompleted,
    ReadyForFinalExitClosure
}
