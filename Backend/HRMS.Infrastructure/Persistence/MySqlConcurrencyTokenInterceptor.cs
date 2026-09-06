using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRMS.Infrastructure.Persistence;

public sealed class MySqlConcurrencyTokenInterceptor(MySqlConcurrencyTokenGenerator generator) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        RotateTokens(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RotateTokens(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void RotateTokens(DbContext? context)
    {
        if (context is null || !string.Equals(context.Database.ProviderName, DatabaseProviderNames.MySql, StringComparison.Ordinal))
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)
                || entry.Entity is not (LeaveRequest or EmployeeLeaveBalance or EmployeeCodeSequence))
                continue;

            entry.Property(nameof(LeaveRequest.RowVersion)).CurrentValue = generator.Create();
        }
    }

    internal void Apply(DbContext context) => RotateTokens(context);
}
