namespace HRMS.Application.Abstractions;

public sealed class LeaveAccrualOptions
{
    public const string SectionName = "LeaveAccrual";
    public bool Enabled { get; init; }
    public int BatchSize { get; init; } = 200;
    public int ClaimLeaseMinutes { get; init; } = 30;
    public int MaxAttempts { get; init; } = 5;
}
