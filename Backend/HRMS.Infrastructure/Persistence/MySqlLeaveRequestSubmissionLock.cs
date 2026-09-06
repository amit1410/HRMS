using System.Data;
using HRMS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRMS.Infrastructure.Persistence;

/// <summary>MySQL employee serialization using the caller's transaction and InnoDB row lock.</summary>
public sealed class MySqlLeaveRequestSubmissionLock(HrmsDbContext db) : ILeaveRequestSubmissionLock, IEmployeeSerializationLock
{
    public async Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(db.Database.ProviderName, DatabaseProviderNames.MySql, StringComparison.Ordinal))
            throw new NotSupportedException("MySQL leave request locking requires a MySQL DbContext.");

        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("MySQL employee serialization requires an active transaction.");
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM `Employees` WHERE `TenantId` = @tenantId AND `Id` = @employeeId FOR UPDATE";
            command.Transaction = transaction.GetDbTransaction();

            var tenantParameter = command.CreateParameter();
            tenantParameter.ParameterName = "@tenantId";
            tenantParameter.DbType = DbType.Guid;
            tenantParameter.Value = tenantId;
            command.Parameters.Add(tenantParameter);

            var employeeParameter = command.CreateParameter();
            employeeParameter.ParameterName = "@employeeId";
            employeeParameter.DbType = DbType.Guid;
            employeeParameter.Value = employeeId;
            command.Parameters.Add(employeeParameter);

            await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }
}
