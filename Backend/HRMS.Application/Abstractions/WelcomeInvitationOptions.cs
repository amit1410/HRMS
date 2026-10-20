namespace HRMS.Application.Abstractions;

public sealed class WelcomeInvitationOptions
{
    public const string SectionName = "WelcomeInvitations";
    public bool Enabled { get; init; } = true;
    public int WorkerIntervalSeconds { get; init; } = 10;
    public int BatchSize { get; init; } = 50;
    public int MaxConcurrency { get; init; } = 4;
    public int MaxAttempts { get; init; } = 3;
    public int LeaseSeconds { get; init; } = 300;
    public int RetryBaseSeconds { get; init; } = 30;
}
