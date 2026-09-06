namespace HRMS.Application.Abstractions;

public interface ILeaveRequestSubmissionRetryPolicy
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken cancellationToken = default);
}

public interface IDatabaseTransientErrorClassifier
{
    bool IsDeadlock(Exception exception);
}

/// <summary>Compatibility name retained while callers migrate to the provider-neutral classifier.</summary>
public interface ILeaveRequestSubmissionDeadlockClassifier : IDatabaseTransientErrorClassifier;
