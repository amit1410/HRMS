using HRMS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Infrastructure.Persistence;

public sealed class MySqlEmployeeCodeSequenceUpdater(HrmsDbContext db, MySqlConcurrencyTokenGenerator generator) : IEmployeeCodeSequenceUpdater
{
    public Task<int> AdvanceAsync(Guid tenantId, Guid sequenceId, long expectedNextNumber, long nextNumber, CancellationToken cancellationToken = default)
    {
        var token = generator.Create();
        return db.EmployeeCodeSequences
            .Where(s => s.Id == sequenceId && s.TenantId == tenantId && s.NextNumber == expectedNextNumber)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.NextNumber, nextNumber)
                    .SetProperty(s => s.RowVersion, token),
                cancellationToken);
    }
}
