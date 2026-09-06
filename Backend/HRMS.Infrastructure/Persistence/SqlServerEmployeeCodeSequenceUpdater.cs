using HRMS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Infrastructure.Persistence;

public sealed class SqlServerEmployeeCodeSequenceUpdater(HrmsDbContext db) : IEmployeeCodeSequenceUpdater
{
    public Task<int> AdvanceAsync(Guid tenantId, Guid sequenceId, long expectedNextNumber, long nextNumber, CancellationToken cancellationToken = default) =>
        db.EmployeeCodeSequences
            .Where(s => s.Id == sequenceId && s.TenantId == tenantId && s.NextNumber == expectedNextNumber)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.NextNumber, nextNumber), cancellationToken);
}
