using HRMS.Application.Abstractions;
using HRMS.Application.Services;

namespace HRMS.Tests;

public sealed class LeaveRequestSubmissionRetryPolicyTests
{
    [Fact]
    public async Task Successful_first_attempt_runs_once()
    {
        var policy = CreatePolicy(out var calls);
        var result = await policy.ExecuteAsync(_ => { calls++; return Task.FromResult(42); });
        Assert.Equal(42, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task One_deadlock_retries_and_succeeds()
    {
        var policy = CreatePolicy(out var calls);
        var result = await policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            if (calls == 1) throw new SyntheticDeadlockException();
            return Task.FromResult(42);
        });
        Assert.Equal(42, result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Two_deadlocks_retry_then_third_attempt_succeeds()
    {
        var policy = CreatePolicy(out var calls);
        var result = await policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            if (calls < 3) throw new SyntheticDeadlockException();
            return Task.FromResult(42);
        });
        Assert.Equal(42, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Third_deadlock_is_propagated_after_three_total_attempts()
    {
        var policy = CreatePolicy(out var calls);
        await Assert.ThrowsAsync<SyntheticDeadlockException>(() => policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            throw new SyntheticDeadlockException();
        }));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Non_deadlock_exception_is_not_retried()
    {
        var policy = CreatePolicy(out var calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            throw new InvalidOperationException("not a deadlock");
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancellation_prevents_next_attempt()
    {
        var policy = CreatePolicy(out var calls);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.ExecuteAsync<int>(ct =>
        {
            calls++;
            cancellation.Cancel();
            throw new SyntheticDeadlockException();
        }, cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Each_retry_invokes_the_attempt_delegate_again()
    {
        var policy = CreatePolicy(out var calls);
        var scopes = new List<int>();
        await policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            scopes.Add(calls);
            if (calls == 1) throw new SyntheticDeadlockException();
            return Task.FromResult(42);
        });
        Assert.Equal([1, 2], scopes);
    }

    private static LeaveRequestSubmissionRetryPolicy CreatePolicy(out int calls)
    {
        calls = 0;
        return new(
            new SyntheticClassifier(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeaveRequestSubmissionRetryPolicy>.Instance);
    }

    private sealed class SyntheticClassifier : ILeaveRequestSubmissionDeadlockClassifier
    {
        public bool IsDeadlock(Exception exception) => exception is SyntheticDeadlockException;
    }

    private sealed class SyntheticDeadlockException : Exception { }
}
