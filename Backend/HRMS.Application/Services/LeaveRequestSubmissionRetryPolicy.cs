using HRMS.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class LeaveRequestSubmissionRetryPolicy : ILeaveRequestSubmissionRetryPolicy
{
    public const int MaximumAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private readonly ILeaveRequestSubmissionDeadlockClassifier _classifier;
    private readonly ILogger<LeaveRequestSubmissionRetryPolicy> _logger;

    public LeaveRequestSubmissionRetryPolicy(
        ILeaveRequestSubmissionDeadlockClassifier classifier,
        ILogger<LeaveRequestSubmissionRetryPolicy> logger)
    {
        _classifier = classifier;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken cancellationToken = default)
    {
        for (var attemptNumber = 1; attemptNumber <= MaximumAttempts; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await attempt(cancellationToken);
            }
            catch (Exception exception) when (_classifier.IsDeadlock(exception))
            {
                if (attemptNumber == MaximumAttempts)
                {
                    _logger.LogWarning("Leave request submission deadlock retry exhausted after {AttemptCount} attempts.", attemptNumber);
                    throw;
                }

                _logger.LogWarning("Leave request submission deadlock detected on attempt {AttemptNumber}; retrying.", attemptNumber);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        throw new InvalidOperationException("The leave request submission retry policy terminated unexpectedly.");
    }
}
