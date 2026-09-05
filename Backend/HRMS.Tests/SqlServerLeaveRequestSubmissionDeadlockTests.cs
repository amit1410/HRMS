using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRMS.Tests;

[Collection("SQL Server Leave Request Concurrency")]
public sealed class SqlServerLeaveRequestSubmissionDeadlockTests
{
    private readonly SqlServerLeaveRequestConcurrencyFixture _fixture;

    public SqlServerLeaveRequestSubmissionDeadlockTests(SqlServerLeaveRequestConcurrencyFixture fixture) => _fixture = fixture;

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Real_sql_server_deadlock_is_error_1205_and_classifier_recognizes_it()
    {
        var deadlock = await RunDeadlockAsync();

        Assert.NotNull(deadlock);
        var sqlException = FindSqlException(deadlock);
        Assert.NotNull(sqlException);
        Assert.Equal(1205, sqlException!.Number);
        Assert.True(new SqlServerLeaveRequestSubmissionDeadlockClassifier().IsDeadlock(deadlock!));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Real_non_deadlock_sql_error_is_rejected_by_classifier()
    {
        Exception? observed = null;
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        try
        {
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT * FROM dbo.__HRMS_DeadlockValidation_MissingTable";
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        Assert.NotNull(observed);
        var sqlException = FindSqlException(observed);
        Assert.NotNull(sqlException);
        Assert.Equal(208, sqlException!.Number);
        Assert.False(new SqlServerLeaveRequestSubmissionDeadlockClassifier().IsDeadlock(observed!));
    }

    private async Task<Exception?> RunDeadlockAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstResourceHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResourceHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = DeadlockParticipantAsync(
            _fixture.EmployeeA,
            _fixture.EmployeeB,
            firstResourceHeld,
            secondResourceHeld,
            timeout.Token);
        var second = DeadlockParticipantAsync(
            _fixture.EmployeeB,
            _fixture.EmployeeA,
            secondResourceHeld,
            firstResourceHeld,
            timeout.Token);

        var results = await Task.WhenAll(first, second);
        var victim = results.SingleOrDefault(exception => FindSqlException(exception)?.Number == 1205);
        if (victim is not null)
            return victim;

        throw new Xunit.Sdk.XunitException(
            $"No deadlock victim with SQL error 1205 was observed. Participant outcomes: {string.Join("; ", results.Select(DescribeException))}");
    }

    private async Task<Exception?> DeadlockParticipantAsync(
        Guid firstEmployeeId,
        Guid secondEmployeeId,
        TaskCompletionSource<bool> firstResourceHeld,
        TaskCompletionSource<bool> otherResourceHeld,
        CancellationToken cancellationToken)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await LockEmployeeAsync(db, firstEmployeeId, cancellationToken);
            firstResourceHeld.TrySetResult(true);
            await otherResourceHeld.Task.WaitAsync(cancellationToken);
            await LockEmployeeAsync(db, secondEmployeeId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            firstResourceHeld.TrySetResult(false);
            otherResourceHeld.TrySetResult(false);
            return exception;
        }
    }

    private async Task LockEmployeeAsync(HrmsDbContext db, Guid employeeId, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT Id
            FROM dbo.Employees WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE TenantId = @tenantId AND Id = @employeeId;
            """;

        var tenantParameter = command.CreateParameter();
        tenantParameter.ParameterName = "@tenantId";
        tenantParameter.Value = _fixture.TenantA;
        command.Parameters.Add(tenantParameter);
        var employeeParameter = command.CreateParameter();
        employeeParameter.ParameterName = "@employeeId";
        employeeParameter.Value = employeeId;
        command.Parameters.Add(employeeParameter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
    }

    private static SqlException? FindSqlException(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
                return sqlException;
        }

        return null;
    }

    private static string DescribeException(Exception? exception)
    {
        if (exception is null)
            return "success";

        var sqlException = FindSqlException(exception);
        return sqlException is null
            ? exception.GetType().Name
            : $"{exception.GetType().Name} -> SqlException({sqlException.Number})";
    }
}
