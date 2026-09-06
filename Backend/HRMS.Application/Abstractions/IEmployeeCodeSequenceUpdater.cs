namespace HRMS.Application.Abstractions;

public interface IEmployeeCodeSequenceUpdater
{
    Task<int> AdvanceAsync(
        Guid tenantId,
        Guid sequenceId,
        long expectedNextNumber,
        long nextNumber,
        CancellationToken cancellationToken = default);
}
