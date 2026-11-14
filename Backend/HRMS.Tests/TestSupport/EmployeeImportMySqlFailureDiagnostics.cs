using MySql.Data.MySqlClient;

namespace HRMS.Tests.TestSupport;

internal static class EmployeeImportMySqlFailureDiagnostics
{
    public const string ProviderAcceptanceTestName =
        "EmployeeImportMySqlProviderAcceptanceEntryPoint.MySql_schema_and_minio_provider_acceptance";

    public static EmployeeImportMySqlFailureDiagnostic Create(string stage, Exception exception, string? failingCheckpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(exception);

        var category = Category(stage, exception);
        return new(
            ProviderAcceptanceTestName,
            stage,
            category,
            ErrorCode(exception),
            Remediation(category),
            failingCheckpoint);
    }

    private static string Category(string stage, Exception exception)
    {
        if (exception is MySqlException mysql && mysql.Number is 1044 or 1045 or 1142 or 1143 or 1227)
            return "DatabaseAuthorization";
        if (exception is TimeoutException)
            return "Timeout";
        if (stage.Contains("migration", StringComparison.OrdinalIgnoreCase))
            return "Migration";
        if (stage.Contains("storage", StringComparison.OrdinalIgnoreCase))
            return "WorkbookStorage";
        if (stage.Contains("worker", StringComparison.OrdinalIgnoreCase) ||
            stage.Contains("employee-import", StringComparison.OrdinalIgnoreCase))
            return "EmployeeImportExecution";
        if (exception is MySqlException)
            return "MySqlProvider";
        if (exception is InvalidOperationException)
            return "AcceptanceInvariant";
        return "ProviderOperation";
    }

    private static string ErrorCode(Exception exception) => exception switch
    {
        MySqlException mysql => $"MYSQL_{mysql.Number}",
        TimeoutException => "TIMEOUT",
        OperationCanceledException => "CANCELED",
        InvalidOperationException => "INVALID_OPERATION",
        _ => "UNEXPECTED_EXCEPTION"
    };

    private static string Remediation(string category) => category switch
    {
        "DatabaseAuthorization" => "Verify the restricted account can use only the three run-scoped schemas and retry the isolated run.",
        "Migration" => "Inspect the private test log for the migration operation and verify the MySQL provider/migration assemblies.",
        "WorkbookStorage" => "Verify the run-scoped encrypted filesystem configuration and key material are available to the test process.",
        "EmployeeImportExecution" => "Inspect the private test log for the employee-import stage; the sanitized report contains no employee data.",
        "Timeout" => "Inspect the private test log for the timed operation and verify the isolated runner has sufficient time.",
        _ => "Inspect the private test log for the named stage; connection strings and exception messages are intentionally omitted."
    };
}

public sealed record EmployeeImportMySqlFailureDiagnostic(
    string TestName,
    string Stage,
    string Category,
    string ErrorCode,
    string Remediation,
    string? FailingCheckpoint = null);
