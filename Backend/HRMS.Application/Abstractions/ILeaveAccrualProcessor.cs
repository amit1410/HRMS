using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

public interface ILeaveAccrualProcessor
{
    Task<LeaveAccrualProcessResult> ProcessAsync(DateOnly throughDate, CancellationToken cancellationToken = default);
}

public sealed record LeaveAccrualProcessResult(int Processed, int Skipped, int Failed, decimal CreditedQuantity);
