namespace HRMS.Application.Abstractions;

public interface ILeaveEntitlementExpiryProcessor
{
    Task<LeaveEntitlementExpiryResult> ProcessAsync(DateOnly throughDate, CancellationToken cancellationToken = default);
}

public sealed record LeaveEntitlementExpiryResult(int Processed, int Skipped, decimal ExpiredQuantity);
