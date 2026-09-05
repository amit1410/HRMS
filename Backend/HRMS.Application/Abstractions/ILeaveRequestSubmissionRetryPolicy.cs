namespace HRMS.Application.Abstractions;

public interface ILeaveRequestSubmissionRetryPolicy
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken cancellationToken = default);
}

public interface ILeaveRequestSubmissionDeadlockClassifier
{
    bool IsDeadlock(Exception exception);
}
