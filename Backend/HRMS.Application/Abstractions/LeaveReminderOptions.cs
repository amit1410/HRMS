namespace HRMS.Application.Abstractions;

public sealed class LeaveReminderOptions
{
    public const string SectionName = "LeaveReminders";
    public bool Enabled { get; init; }
    public int InitialDelayHours { get; init; } = 24;
    public int RepeatIntervalHours { get; init; } = 24;
    public int WorkerIntervalMinutes { get; init; } = 15;
    public int BatchSize { get; init; } = 100;
    public int MaxAttempts { get; init; } = 3;
    public int ClaimLeaseMinutes { get; init; } = 30;

    public string? Validate()
    {
        if (InitialDelayHours < 1 || RepeatIntervalHours < 1 || WorkerIntervalMinutes < 1 || BatchSize < 1 || MaxAttempts < 1 || ClaimLeaseMinutes < 1)
            return $"{SectionName} values must be positive.";
        return null;
    }
}
