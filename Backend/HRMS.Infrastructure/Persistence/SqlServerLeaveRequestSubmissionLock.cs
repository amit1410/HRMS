using System.Data;
using HRMS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRMS.Infrastructure.Persistence;

/// <summary>
/// SQL Server implementation of the submission employee scope. SQLite is accepted only for the isolated
/// test harness, where SQL Server locking hints do not exist; production SQL Server never takes this path.
/// </summary>
public sealed class SqlServerLeaveRequestSubmissionLock(HrmsDbContext db) : ILeaveRequestSubmissionLock
{
    public async Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlite())
            return;
        if (!db.Database.IsSqlServer())
            throw new NotSupportedException("Leave request submission locking is implemented only for SQL Server.");

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM [Employees] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = @tenantId AND [Id] = @employeeId";
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
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
