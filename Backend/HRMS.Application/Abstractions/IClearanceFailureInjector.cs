namespace HRMS.Application.Abstractions;

/// <summary>Internal testability seam. Production composition leaves this null; it is never exposed through configuration or HTTP.</summary>
public interface IClearanceFailureInjector
{
    void BeforeTaskCommit();
    void BeforeCompletionCommit();
    void BeforeAssetCommit();
}
