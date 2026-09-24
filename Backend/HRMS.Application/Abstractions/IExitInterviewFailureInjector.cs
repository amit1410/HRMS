namespace HRMS.Application.Abstractions;

/// <summary>Internal test seam only. Production composition leaves this null and exposes no failure switch.</summary>
public interface IExitInterviewFailureInjector
{
    void BeforeEmployeeSubmissionCommit();
    void BeforeHrCompletionCommit();
    void BeforeReopenCommit();
}
