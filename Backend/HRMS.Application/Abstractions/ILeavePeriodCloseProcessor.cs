namespace HRMS.Application.Abstractions;

public interface ILeavePeriodCloseProcessor
{
    Task<LeavePeriodCloseProcessResult> ProcessAsync(Guid sourceLeavePeriodId, DateOnly asOf, CancellationToken cancellationToken = default);
}

public sealed record LeavePeriodCloseProcessResult(int Processed, int Skipped, int Failed, decimal CarriedQuantity, decimal LapsedQuantity);
