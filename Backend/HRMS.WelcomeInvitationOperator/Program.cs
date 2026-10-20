using HRMS.Application;
using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Domain.Entities;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

return await Operator.RunAsync(args);

internal static class Operator
{
    private const string ApiUserSecretsId = "af221e70-58f4-4c5b-bbf0-034f30422395";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var a = Args.Parse(args);
            var configuration = BuildConfiguration(args);
            if (!Guid.TryParse(a.Required("tenant-id"), out var tenantId) || tenantId == Guid.Empty) throw new ArgumentException("--tenant-id must be a non-empty GUID.");
            if (!Guid.TryParse(a.Required("batch-id"), out var batchId) || batchId == Guid.Empty) throw new ArgumentException("--batch-id must be a non-empty GUID.");
            var code = a.Required("tenant-code"); var shardKey = a.Required("shard"); var database = a.Required("database");
            ValidateConnectionConfiguration(configuration);
            var allow = configuration.GetSection("MigrationHost:AllowedTenants").Get<string[]>() ?? [];
            if (!allow.Contains($"{code}:{tenantId}:{shardKey}:{database}", StringComparer.Ordinal)) throw new InvalidOperationException("Target is not in MigrationHost:AllowedTenants (expected TenantCode:TenantId:ShardKey:Database).");
            if (a.Command == "process") ValidateSmtp(configuration);
            else if (a.Command != "preview") throw new ArgumentException("Command must be preview or process.");

            var startupConfiguration = a.Command == "preview" ? PreviewConfiguration(configuration) : configuration;
            using var host = BuildHost(startupConfiguration, Environments.Production, readOnlyPreview: a.Command == "preview");

            // Catalog resolution is read-only and mirrors MigrationHost's trusted allowlist plus exact row checks.
            Tenant tenant;
            await using (var catalog = host.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
            {
                var db = catalog.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();
                tenant = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == tenantId);
                if (tenant.TenantCode != code || tenant.ShardKey != shardKey || tenant.Status != TenantStatus.Active) throw new InvalidOperationException("Catalog tenant identity or active status does not match supplied target.");
            }
            var descriptor = new ShardDescriptor(tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status, tenant.DatabaseProvider);
            await using var scope = host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IShardContext>().Use(descriptor);
            scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().Use(tenantId);
            var tenantDb = scope.ServiceProvider.GetRequiredService<HrmsDbContext>();
            var actualDatabase = tenantDb.Database.GetDbConnection().Database;
            if (!string.Equals(actualDatabase, database, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Tenant physical database mismatch.");
            var actualProvider = tenantDb.Database.ProviderName ?? "";
            var providerMatches = tenant.DatabaseProvider switch
            {
                DatabaseProviderType.SqlServer => actualProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase),
                DatabaseProviderType.MySql => actualProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            if (!providerMatches) throw new InvalidOperationException("Tenant catalog provider does not match the selected tenant database provider.");
            var processor = scope.ServiceProvider.GetRequiredService<IWelcomeInvitationProcessor>();
            if (a.Command == "preview")
            {
                var result = await processor.PreviewBatchAsync(tenantId, batchId);
                scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("WelcomeInvitationOperator").LogInformation("Operator invitation batch previewed TenantCode={TenantCode} TenantId={TenantId} Shard={Shard} Database={Database} BatchId={BatchId} Eligible={EligibleCount} Excluded={ExcludedCount} Outcome={Outcome}", code, tenantId, shardKey, database, batchId, result.EligibleCount, result.Excluded.Count, "ReadOnlyPreview");
                Console.WriteLine($"tenant={tenant.TenantCode} ({tenant.Id}) shard={tenant.ShardKey} database={actualDatabase}\nbatch={batchId}\neligible={result.EligibleCount}");
                foreach (var row in result.Eligible) Console.WriteLine($"eligible {row.AttemptId} {row.MaskedRecipient} {row.Status}");
                foreach (var row in result.Excluded) Console.WriteLine($"excluded {row.AttemptId} {row.MaskedRecipient} reason={row.Reason}");
                return 0;
            }
            if (!Guid.TryParse(a.Required("attempt-id"), out var attemptId) || attemptId == Guid.Empty) throw new ArgumentException("--attempt-id must be a non-empty GUID.");
            if (!string.Equals(a.Get("confirm"), $"PROCESS {tenantId} {batchId} {attemptId}", StringComparison.Ordinal)) throw new InvalidOperationException($"Confirmation required: --confirm 'PROCESS {tenantId} {batchId} {attemptId}'");
            await processor.ProcessSingleAttemptAsync(tenantId, batchId, attemptId, true);
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("WelcomeInvitationOperator").LogInformation("Operator invitation attempt processed TenantCode={TenantCode} TenantId={TenantId} Shard={Shard} Database={Database} BatchId={BatchId} AttemptId={AttemptId}", code, tenantId, shardKey, database, batchId, attemptId);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Operator failed: {ex.Message}"); return 2; }
    }

    internal static IConfiguration BuildConfiguration(
        string[] args,
        string? apiConfigurationPath = null,
        Action<IConfigurationBuilder>? addDevelopmentSecrets = null,
        string? environmentName = null)
    {
        var environment = environmentName ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environments.Production;
        apiConfigurationPath ??= Path.Combine(Environment.CurrentDirectory, "Backend", "HRMS.API");
        var builder = new ConfigurationBuilder()
            .SetBasePath(apiConfigurationPath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false);

        if (string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            if (addDevelopmentSecrets is null)
                builder.AddUserSecrets(ApiUserSecretsId);
            else
                addDevelopmentSecrets(builder);
        }

        return builder.AddEnvironmentVariables().AddCommandLine(args).Build();
    }

    internal static void ValidateConnectionConfiguration(IConfiguration configuration)
    {
        // Catalog selection is configuration based. Tenant provider selection happens only after
        // the authorized catalog row is read and its DatabaseProvider is placed on the shard descriptor.
        var catalogProvider = configuration["Database:CatalogProvider"] ?? configuration["Database:Provider"] ?? "SqlServer";
        var catalogKey = string.Equals(catalogProvider, "Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "ConnectionStrings:SqliteCatalog"
            : "ConnectionStrings:Catalog";
        RequireConnectionString(configuration, catalogKey);

        // Do not infer the tenant provider here. Requiring a SQL Server template based on the
        // global default can reject a MySQL catalog tenant before its authoritative metadata is read.
        // ShardConnectionStringFactory validates the selected provider/template after catalog lookup.
    }

    private static void RequireConnectionString(IConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
            throw new InvalidOperationException($"{key} is required and must not be empty.");
    }

    internal static IHost BuildHost(IConfiguration configuration, string environmentName, bool readOnlyPreview = false, bool isolatedTestHost = false)
    {
        var builder = Host.CreateDefaultBuilder();
        if (isolatedTestHost)
        {
            // Windows adds EventLog logging to Production hosts. Isolated tests have no
            // Event Log permission, so keep console logging while omitting that sink.
            builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        }

        return builder
            .UseEnvironment(environmentName)
            .ConfigureAppConfiguration((_, builder) => builder.AddConfiguration(configuration))
            .ConfigureServices((context, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure(
                    context.Configuration,
                    context.HostingEnvironment,
                    includeDeliveryInfrastructure: !readOnlyPreview);
                // This console process has no authenticated request principal. Deny all permission checks;
                // operator execution is authorized only by its explicit tenant allowlist and confirmation.
                services.AddScoped<ICurrentAuthorizationContext, DenyOperatorAuthorizationContext>();
                services.AddScoped<ITenantExecutionContext, OperatorTenantContext>();
                services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ITenantExecutionContext>());
                // Infrastructure is shared with the API, but background workers must never start here.
                services.RemoveAll<IHostedService>();
            })
            .Build();
    }

    internal static IConfiguration PreviewConfiguration(IConfiguration configuration) => new ConfigurationBuilder()
        .AddConfiguration(configuration)
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Fake",
            ["PasswordRecoveryProviders:EmailProvider"] = "Fake"
        })
        .Build();

    internal static void ValidateSmtp(IConfiguration c)
    {
        static string? Get(IConfiguration x, string a, string b) => x[a] ?? x[b];
        var provider = c["Email:Provider"] ?? c["PasswordRecoveryProviders:EmailProvider"] ?? "Smtp";
        if (!string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Operator requires configured SMTP delivery; Fake provider is not permitted.");
        var port = Get(c, "Email:Smtp:Port", "Email:SmtpPort"); var ssl = Get(c, "Email:Smtp:EnableSsl", "Email:EnableSsl");
        if (string.IsNullOrWhiteSpace(Get(c, "Email:Smtp:Host", "Email:SmtpHost")) || string.IsNullOrWhiteSpace(Get(c, "Email:Smtp:Username", "Email:SmtpUsername")) || string.IsNullOrWhiteSpace(Get(c, "Email:Smtp:Password", "Email:SmtpPassword")) || string.IsNullOrWhiteSpace(c["Email:FromEmail"]) || string.IsNullOrWhiteSpace(c["Email:FromName"]) || !int.TryParse(port, out var smtpPort) || smtpPort != 587 || !bool.TryParse(ssl, out var enableSsl) || !enableSsl) throw new InvalidOperationException("Complete SMTP configuration is required (host, port 587, credentials, sender, and STARTTLS).");
        Console.WriteLine("SMTP configuration present; credentials are not displayed.");
    }
}

internal sealed class DenyOperatorAuthorizationContext : ICurrentAuthorizationContext
{
    public bool HasAnyPermission(params string[] permissions) => false;
}

internal sealed class OperatorTenantContext : ITenantExecutionContext
{
    public Guid? TenantId { get; private set; } public Guid? UserId => null; public bool HasTenant => TenantId.HasValue;
    public void Use(Guid tenantId) { if (tenantId == Guid.Empty || TenantId is not null && TenantId != tenantId) throw new InvalidOperationException("Invalid operator tenant identity."); TenantId = tenantId; }
}

internal sealed class Args
{
    private readonly Dictionary<string,string> _values = new(StringComparer.OrdinalIgnoreCase);
    public string Command { get; private init; } = "";
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "tenant-id", "tenant-code", "shard", "database", "batch-id", "attempt-id", "confirm" };
    public static Args Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("preview" or "process")) throw new ArgumentException("First argument must be preview or process.");
        var a = new Args { Command = args[0] };
        for (var i = 1; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2) throw new ArgumentException($"Unexpected argument '{token}'.");
            var key = token[2..];
            if (!Allowed.Contains(key)) throw new ArgumentException($"Unknown argument '--{key}'.");
            if (!a._values.TryAdd(key, "")) throw new ArgumentException($"Duplicate argument '--{key}'.");
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Value required for '--{key}'.");
            a._values[key] = args[i];
        }
        foreach (var key in new[] { "tenant-id", "tenant-code", "shard", "database", "batch-id" }) a.Required(key);
        if (!Guid.TryParse(a.Required("tenant-id"), out var tenantId) || tenantId == Guid.Empty) throw new ArgumentException("--tenant-id must be a non-empty GUID.");
        if (!Guid.TryParse(a.Required("batch-id"), out var batchId) || batchId == Guid.Empty) throw new ArgumentException("--batch-id must be a non-empty GUID.");
        if (a.Command == "preview" && a.Get("confirm") is not null) throw new ArgumentException("--confirm is only valid with process.");
        if (a.Command == "process" && string.IsNullOrWhiteSpace(a.Get("confirm"))) throw new ArgumentException("--confirm is required for process.");
        if (a.Command == "process")
        {
            if (!Guid.TryParse(a.Required("attempt-id"), out var attemptId) || attemptId == Guid.Empty) throw new ArgumentException("--attempt-id must be a non-empty GUID.");
            if (!string.Equals(a.Get("confirm"), $"PROCESS {tenantId} {batchId} {attemptId}", StringComparison.Ordinal)) throw new ArgumentException($"Confirmation must be exactly 'PROCESS {tenantId} {batchId} {attemptId}'.");
        }
        if (a.Command == "preview" && a.Get("attempt-id") is not null) throw new ArgumentException("--attempt-id is only valid with process.");
        return a;
    }
    public string Required(string key) => Get(key) ?? throw new ArgumentException($"--{key} is required.");
    public string? Get(string key) => _values.GetValueOrDefault(key);
}
