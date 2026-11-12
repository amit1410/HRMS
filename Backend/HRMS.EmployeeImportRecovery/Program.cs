using HRMS.API.Controllers;
using HRMS.Application;
using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Data;
using System.Data.Common;
using HRMS.Infrastructure.Persistence;

const string targetHost = "web1.localhost";
const string targetTenantCode = "WEB1";
var targetOperationId = Guid.Parse("b89d04aa-1419-4c85-a7e3-48ab1d92d111");

if (!TryArgs(args, out var parsed)) return Fail("invocation.invalid_arguments");
if (parsed.OperationId != targetOperationId) return Fail("operation_id_not_allowlisted");
if (parsed.Mode == "apply")
{
    if (string.IsNullOrWhiteSpace(parsed.Operator) || string.IsNullOrWhiteSpace(parsed.Reason) || string.IsNullOrWhiteSpace(parsed.OwnerApprovalId))
        return Fail("apply_requires_operator_reason_and_owner_approval_id");
    Console.WriteLine($"To authorize recovery of WEB1 operation {targetOperationId:D}, type the exact confirmation phrase:");
    if (!string.Equals(Console.ReadLine(), $"APPLY WEB1 EMPLOYEE IMPORT RECOVERY {targetOperationId:D} {parsed.OwnerApprovalId}", StringComparison.Ordinal))
        return Fail("explicit_confirmation_mismatch");
}

var tempProbe = ProbeTemporaryFileAccess();
Console.WriteLine(JsonSerializer.Serialize(tempProbe));
if (!tempProbe.Passed) return Fail("diagnostic.temp_file_probe_failed");
var apiDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Backend", "HRMS.API"));
if (!Directory.Exists(apiDirectory)) return Fail("configuration.api_content_root_missing");
var configuration = new ConfigurationBuilder()
    .SetBasePath(apiDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
    .AddUserSecrets(typeof(EmployeeBulkImportExecutionController).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();
var externalConfig = configuration["EmployeeImportStorage:ConfigurationFile"];
if (!string.IsNullOrWhiteSpace(externalConfig))
{
    if (!Path.IsPathFullyQualified(externalConfig)) return Fail("configuration.storage_secret_path_invalid");
    configuration = new ConfigurationBuilder().AddConfiguration(configuration)
        .AddJsonFile(externalConfig, optional: false, reloadOnChange: false).Build();
}
var worker = configuration.GetSection(EmployeeBulkImportWorkerOptions.SectionName).Get<EmployeeBulkImportWorkerOptions>()
    ?? new EmployeeBulkImportWorkerOptions();
if (worker.Enabled) return Fail("worker.enabled");
try { _ = EmployeeImportStorageOptions.LoadAndValidate(configuration, new DevelopmentEnvironment(apiDirectory)); }
catch { return Fail("configuration.employee_import_storage_invalid"); }

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IConfiguration>(configuration);
services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment(apiDirectory));
services.AddApplication();
services.AddInfrastructure(configuration, new DevelopmentEnvironment(apiDirectory), includeDeliveryInfrastructure: false);
services.AddScoped<OperatorTenantContext>();
services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<OperatorTenantContext>());
services.AddScoped<ITenantExecutionContext>(provider => provider.GetRequiredService<OperatorTenantContext>());
// Build a plain service provider. No IHost is constructed and no hosted worker is started.
using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
try
{
    await using var catalogScope = root.CreateAsyncScope();
    var catalog = catalogScope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();
    var routeMatches = await catalog.Tenants.AsNoTracking().Where(x => x.Host == targetHost).Take(2)
        .Select(x => new TenantRoute(x.Id, x.TenantCode, x.Host, x.ShardKey, x.Status, x.DatabaseProvider)).ToListAsync();
    if (routeMatches.Count != 1) return Fail("catalog.host_match_count_invalid");
    var route = routeMatches[0];
    if (route.TenantCode != targetTenantCode || route.Status != TenantStatus.Active ||
        route.DatabaseProvider != parsed.Provider || !string.Equals(route.ShardKey, parsed.Shard, StringComparison.Ordinal))
        return Fail("catalog.route_mismatch");
    var resolved = await catalogScope.ServiceProvider.GetRequiredService<ITenantShardResolver>().ResolveByHostAsync(targetHost);
    if (resolved is null || resolved.TenantId != route.Id || resolved.TenantCode != targetTenantCode ||
        resolved.DatabaseProvider != parsed.Provider || !string.Equals(resolved.ShardKey, parsed.Shard, StringComparison.Ordinal))
        return Fail("catalog.resolver_mismatch");

    await using var scope = root.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<OperatorTenantContext>().Use(route.Id);
    scope.ServiceProvider.GetRequiredService<IShardContext>().Use(resolved);
    var recovery = scope.ServiceProvider.GetRequiredService<IEmployeeImportRecoveryService>();
    var report = parsed.Mode == "apply"
        ? await recovery.RecoverAsync(targetOperationId, route.Id, parsed.Operator!, parsed.Reason!, explicitlyApproved: true)
        : await recovery.DryRunAsync(targetOperationId, route.Id);
    if (parsed.Mode == "dry-run")
    {
        var migrationStatus = await ReadRecoveryAuditMigrationStatusAsync(scope.ServiceProvider);
        report = report with
        {
            RecoveryAuditMigrationApplied = migrationStatus.Applied,
            MigrationStatusErrorCategory = migrationStatus.ErrorCategory
        };
    }
    Console.WriteLine(JsonSerializer.Serialize(report));
    return report.Passed ? 0 : 2;
}
catch
{
    return Fail("recovery.unexpected_error");
}

static TempFileProbeResult ProbeTemporaryFileAccess()
{
    var tempDirectory = Path.GetTempPath();
    var probePath = Path.Combine(tempDirectory, $"hrms-recovery-temp-probe-{Guid.NewGuid():N}.tmp");
    var createSucceeded = false;
    try
    {
        using (var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.WriteByte(0x48);
            stream.Flush();
        }
        createSucceeded = true;
        File.Delete(probePath);
        return new(true, "temp_file.create_delete", "file", "current_process", null, null);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
        System.Security.SecurityException or ArgumentException or NotSupportedException)
    {
        try { if (File.Exists(probePath)) File.Delete(probePath); }
        catch { }
        return new(false, createSucceeded ? "temp_file.delete" : "temp_file.create", "file",
            "current_process", exception.GetType().Name, $"0x{exception.HResult:X8}");
    }
}

static async Task<(bool? Applied, string? ErrorCategory)> ReadRecoveryAuditMigrationStatusAsync(IServiceProvider serviceProvider)
{
    var db = serviceProvider.GetRequiredService<HrmsDbContext>();
    var connection = db.Database.GetDbConnection();
    var closeAfter = connection.State == ConnectionState.Closed;
    try
    {
        if (closeAfter) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = @migrationId LIMIT 1";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@migrationId";
        parameter.Value = "20261104173339_AddEmployeeImportRecoveryAudit";
        command.Parameters.Add(parameter);
        return (await command.ExecuteScalarAsync() is not null, null);
    }
    catch (DbException)
    {
        return (null, "migration_status.database_query_failed");
    }
    catch (InvalidOperationException)
    {
        return (null, "migration_status.database_query_failed");
    }
    finally
    {
        if (closeAfter && connection.State != ConnectionState.Closed)
            await connection.CloseAsync();
    }
}

static bool TryArgs(string[] args, out RecoveryArgs result)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length || !values.TryAdd(args[i], args[++i]))
        { result = default!; return false; }
    }
    var mode = values.ContainsKey("--apply") ? "apply" : "dry-run";
    if (values.ContainsKey("--apply") && values["--apply"] != "true") { result = default!; return false; }
    if (values.ContainsKey("--dry-run") && (values["--dry-run"] != "true" || values.ContainsKey("--apply")))
    { result = default!; return false; }
    if (!Guid.TryParse(values.GetValueOrDefault("--operation"), out var operationId) ||
        !Enum.TryParse<DatabaseProviderType>(values.GetValueOrDefault("--provider"), true, out var provider) ||
        string.IsNullOrWhiteSpace(values.GetValueOrDefault("--shard")))
    { result = default!; return false; }
    result = new(mode, operationId, provider, values["--shard"], values.GetValueOrDefault("--operator"),
        values.GetValueOrDefault("--reason"), values.GetValueOrDefault("--owner-approval-id"));
    return true;
}

static int Fail(string code)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Passed = false, Applied = false, Outcome = "blocked", Blockers = new[] { code } }));
    return 2;
}

sealed record TempFileProbeResult(bool Passed, string OperationCategory, string ResourceType,
    string ProcessIdentity, string? ExceptionType, string? SafeHResult);
sealed record RecoveryArgs(string Mode, Guid OperationId, DatabaseProviderType Provider, string Shard,
    string? Operator, string? Reason, string? OwnerApprovalId);
sealed record TenantRoute(Guid Id, string TenantCode, string Host, string ShardKey, TenantStatus Status, DatabaseProviderType DatabaseProvider);

sealed class OperatorTenantContext : ITenantExecutionContext
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId => null;
    public bool HasTenant => TenantId.HasValue;
    public void Use(Guid tenantId) => TenantId = tenantId != Guid.Empty ? tenantId : throw new ArgumentException("Tenant is required.", nameof(tenantId));
}

sealed class DevelopmentEnvironment(string contentRoot) : IHostEnvironment, IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "HRMS.API";
    public string ContentRootPath { get; set; } = contentRoot;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
    public string WebRootPath { get; set; } = contentRoot;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
}
