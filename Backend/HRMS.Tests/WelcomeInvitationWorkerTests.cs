using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Communication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRMS.Tests;

public sealed class WelcomeInvitationWorkerTests
{
    [Fact]
    public async Task Disabled_worker_logs_effective_configuration_and_does_not_start_cycles()
    {
        var messages = new List<string>();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var worker = new WelcomeInvitationWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            new WelcomeInvitationOptions { Enabled = false, WorkerIntervalSeconds = 7 }, new RecordingLogger(messages));

        await worker.StartAsync(CancellationToken.None);

        Assert.Contains(messages, x => x.Contains("Enabled=False") && x.Contains("ProcessingIntervalSeconds=7"));
        Assert.DoesNotContain(messages, x => x.Contains("cycle started"));
    }

    [Fact]
    public async Task Catalog_failure_is_logged_and_does_not_silently_end_worker_cycle()
    {
        var messages = new List<string>();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var worker = new WelcomeInvitationWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            new WelcomeInvitationOptions { Enabled = true, WorkerIntervalSeconds = 30 }, new RecordingLogger(messages));
        using var cancellation = new CancellationTokenSource();

        await worker.StartAsync(cancellation.Token);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(messages, x => x.Contains("cycle started"));
        Assert.Contains(messages, x => x.Contains("cycle failed before completion") && x.Contains("ExceptionType=InvalidOperationException"));
    }

    private sealed class RecordingLogger(List<string> messages) : ILogger<WelcomeInvitationWorker>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (messages) messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception.Message));
        }
    }
}
