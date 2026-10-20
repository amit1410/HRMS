using System.Diagnostics;
using System.Reflection;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySql.EntityFrameworkCore.Extensions;

namespace HRMS.Tests;

public sealed class WelcomeInvitationDiagnosticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failure_log_redacts_raw_literals_and_generated_parameters_and_never_attaches_exception(bool rawSql)
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        const string email = "private.employee@example.test";
        const string name = "Private O'Brien";
        const string password = "diagnostic-secret-password";
        const string code = "PRIVATE-EMPLOYEE-CODE";
        var options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL($"Server=127.0.0.1;Database=translation_only;User=private_login;Password={password}")
            .Options;
        using var db = new HrmsDbContext(options, new TestTenantContext(tenantId));
        IQueryable query = rawSql
            ? db.Users.FromSqlRaw<User>($"SELECT u.* FROM `Users` AS u WHERE u.`Id` IN ('{employeeId:D}') AND u.`Email` = '{email}' AND u.`FirstName` = 'Private O''Brien'")
            : db.Employees.Where(e => e.Id == employeeId && e.Email == email && e.FirstName == name && e.EmployeeCode == code);
        var logger = new RecordingLogger();
        var service = new WelcomeInvitationService(db, new TestTenantContext(tenantId), null!, TimeProvider.System, logger);
        using var activity = new Activity("welcome-diagnostic-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var exception = new InvalidOperationException($"Credentials: private_login/{password}; employee={employeeId}; {email}",
            new ProviderFailure($"tenant={tenantId}; {name}; {code}"));

        // Exercise the existing failure sink with provider-generated SQL; no database connection is opened.
        typeof(WelcomeInvitationService).GetMethod("LogFailure", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(service, ["Portal account enrichment", exception, query]);

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Portal account enrichment", entry.Properties["QueryStage"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.Properties["ExceptionType"]);
        Assert.Equal(1064, entry.Properties["MySqlErrorNumber"]);
        Assert.Equal(activity.TraceId.ToString(), entry.Properties["CorrelationId"]);
        Assert.Contains("SELECT", entry.Message);
        Assert.Contains("<redacted>", entry.Message);
        var allOutput = entry.Message + string.Join("|", entry.Properties.Select(p => $"{p.Key}:{p.Value}"));
        foreach (var sensitive in new[] { tenantId.ToString(), employeeId.ToString(), email, name, "O''Brien", password, code, "private_login" })
            Assert.DoesNotContain(sensitive, allOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redaction_preserves_the_observed_empty_in_list_and_select_columns()
    {
        const string sql = "SELECT `h`.`Id`, `h`.`Email` FROM (SELECT u.* FROM `Users` AS u WHERE u.`Id` IN ()) AS `h` WHERE `h`.`TenantId` = @tenant";
        var redacted = (string)typeof(WelcomeInvitationService)
            .GetMethod("RedactSql", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [sql])!;

        Assert.Contains("u.`Id` IN ()", redacted);
        Assert.Contains("SELECT `h`.`Id`, `h`.`Email`", redacted);
    }

    private sealed class ProviderFailure(string message) : Exception(message)
    {
        public int Number => 1064;
    }

    private sealed record Entry(LogLevel Level, string Message, Exception? Exception, Dictionary<string, object?> Properties);

    private sealed class RecordingLogger : ILogger<WelcomeInvitationService>
    {
        public List<Entry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new(logLevel, formatter(state, exception), exception,
                ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(p => p.Key, p => p.Value)));
    }
}
