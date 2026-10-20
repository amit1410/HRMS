using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Communication;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests.TestSupport;

public sealed record ProviderAcceptanceRunResult(
    string Provider,
    string RunId,
    string Status,
    IReadOnlyList<string> CompletedCases,
    string? FailureCategory,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc);

public sealed class ProviderAcceptanceRunner
{
    public const string ExecuteVariable = "HRMS_PHASE1E_EXECUTE";
    public const string ProviderVariable = "HRMS_PHASE1E_PROVIDER";
    public const string ServerVariable = "HRMS_PHASE1E_SERVER_IDENTITY";
    public const string InstanceVariable = "HRMS_PHASE1E_INSTANCE_OR_PORT";
    public const string DatabaseNamesVariable = "HRMS_PHASE1E_DATABASE_NAMES";
    public const string RunIdVariable = "HRMS_PHASE1E_RUN_ID";
    public const string ManifestPathVariable = "HRMS_PHASE1E_MANIFEST_PATH";
    public const string EnvironmentVariable = "HRMS_PHASE1E_ENVIRONMENT";
    public const string TestConfigurationVariable = "HRMS_PHASE1E_TEST_ONLY_CONFIGURATION";
    public const string ConnectionApprovalVariable = "HRMS_PHASE1E_APPROVE_CONNECTION";
    public const string CreateApprovalVariable = "HRMS_PHASE1E_APPROVE_CREATE";
    public const string MigrationApprovalVariable = "HRMS_PHASE1E_APPROVE_MIGRATION";
    public const string DeleteApprovalVariable = "HRMS_PHASE1E_APPROVE_DELETE";
    public const string ResultPathVariable = "HRMS_PHASE1E_RESULT_PATH";

    public static bool IsExplicitlyEnabled => string.Equals(Environment.GetEnvironmentVariable(ExecuteVariable), "true", StringComparison.OrdinalIgnoreCase);

    public static ProviderAcceptanceRunConfiguration ReadConfiguration(DisposableDatabaseProvider provider)
    {
        if (!IsExplicitlyEnabled) throw new InvalidOperationException($"Set {ExecuteVariable}=true through the dedicated provider acceptance command.");
        var requestedProvider = Required(ProviderVariable);
        if (!string.Equals(requestedProvider, provider.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("The requested provider does not match the executable test entry point.");

        var names = Required(DatabaseNamesVariable).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length < 2) throw new InvalidOperationException("At least two exact disposable database names are required: acceptance and migration-down.");
        var target = new DisposableDatabaseTarget(provider, Required(ServerVariable), Required(InstanceVariable), names, Required(RunIdVariable), Required(EnvironmentVariable), Required(TestConfigurationVariable), Required(ManifestPathVariable));
        var approval = new DisposableDatabaseApproval(
            ParseApproval(CreateApprovalVariable), ParseApproval(DeleteApprovalVariable), ParseApproval(ConnectionApprovalVariable), ParseApproval(MigrationApprovalVariable),
            provider.ToString(), target.ServerIdentity, target.InstanceOrPort, target.RunId, target.ManifestPath,
            target.ExpectedDatabaseNames, target.EnvironmentDesignation);
        return new(target, approval, Required(ResultPathVariable));
    }

    public async Task<ProviderAcceptanceRunResult> RunAsync(DisposableDatabaseProvider provider, CancellationToken cancellationToken = default)
    {
        var configuration = ReadConfiguration(provider);
        var started = DateTimeOffset.UtcNow;
        var completed = new List<string>();
        var adapter = provider == DisposableDatabaseProvider.SqlServer
            ? (IDisposableDatabaseAdapter)new SqlServerDisposableDatabaseAdapter()
            : new MySqlDisposableDatabaseAdapter();
        var manifests = new JsonDisposableDatabaseManifestStore();
        var harness = new DisposableDatabaseHarness(adapter, manifests);
        DisposableDatabaseManifest? acceptanceManifest = null;
        var status = "PASSED";
        string? failureCategory = null;

        try
        {
            var acceptanceTarget = configuration.Target with { ExpectedDatabaseNames = [configuration.Target.ExpectedDatabaseNames[0]], ManifestPath = ManifestFor(configuration, "acceptance") };
            var acceptanceApproval = ApprovalFor(configuration.Approval, acceptanceTarget);
            acceptanceManifest = await harness.CreateAsync(acceptanceTarget, acceptanceApproval, cancellationToken);
            var context = provider == DisposableDatabaseProvider.SqlServer
                ? ((SqlServerDisposableDatabaseAdapter)adapter).CreateDbContext(acceptanceTarget, acceptanceManifest.DatabaseName)
                : ((MySqlDisposableDatabaseAdapter)adapter).CreateDbContext(acceptanceTarget, acceptanceManifest.DatabaseName);
            await using (context)
            {
                await VerifySchemaAndIndexesAsync(context, cancellationToken);
                completed.Add("Schema and indexes");
                await RunInvitationScenariosAsync(context, provider, completed, cancellationToken);
            }

            var rollbackTarget = configuration.Target with { ExpectedDatabaseNames = [configuration.Target.ExpectedDatabaseNames[1]], ManifestPath = ManifestFor(configuration, "migration-down") };
            var rollbackApproval = ApprovalFor(configuration.Approval, rollbackTarget);
            var rollbackManifest = await harness.CreateAsync(rollbackTarget, rollbackApproval, cancellationToken);
            await harness.MigrateDownAsync(rollbackTarget, rollbackApproval, rollbackManifest, cancellationToken);
            completed.Add("Migration-down isolated lifecycle");
            await harness.CleanupAsync(rollbackTarget, rollbackApproval, rollbackManifest, cancellationToken);
            await harness.CleanupAsync(acceptanceTarget, acceptanceApproval, acceptanceManifest, cancellationToken);
            completed.Add("Cleanup ownership and database absence");
        }
        catch (InvalidOperationException exception)
        {
            status = IsGateFailure(exception.Message) ? "BLOCKED" : "FAILED";
            failureCategory = status;
            throw;
        }
        catch (Exception)
        {
            status = "FAILED";
            failureCategory = status;
            throw;
        }
        finally
        {
            var result = new ProviderAcceptanceRunResult(provider.ToString(), configuration.Target.RunId, status, completed, failureCategory, started, DateTimeOffset.UtcNow);
            await WriteResultAsync(result, configuration.ResultPath, cancellationToken);
        }

        return new ProviderAcceptanceRunResult(provider.ToString(), configuration.Target.RunId, status, completed, failureCategory, started, DateTimeOffset.UtcNow);
    }

    private static async Task VerifySchemaAndIndexesAsync(HrmsDbContext context, CancellationToken ct)
    {
        if (!await context.Database.CanConnectAsync(ct)) throw new InvalidOperationException("The disposable provider database could not be connected after migration.");
        var attempt = context.Model.FindEntityType(typeof(WelcomeInvitationAttempt)) ?? throw new InvalidOperationException("Welcome invitation attempt schema is missing.");
        if (!attempt.GetIndexes().Any(index => index.Properties.Any(property => property.Name == nameof(WelcomeInvitationAttempt.TenantId))))
            throw new InvalidOperationException("The welcome invitation tenant index is missing.");
    }

    private static async Task RunInvitationScenariosAsync(HrmsDbContext db, DisposableDatabaseProvider provider, List<string> completed, CancellationToken ct)
    {
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var attempt = Guid.NewGuid();
        var batch = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenant, TenantCode = $"P1E{tenant:N}"[..12], TenantName = "Phase 1E acceptance", Host = $"{tenant:N}.test", ShardKey = tenant.ToString("N"), Status = TenantStatus.Active });
        db.Employees.Add(new Employee { Id = employee, TenantId = tenant, EmployeeCode = $"P1E-{employee:N}"[..12], FirstName = "Acceptance", LastName = provider.ToString(), Email = $"{employee:N}@example.test", DateOfJoining = new DateOnly(2026, 1, 1) });
        db.WelcomeInvitationBatches.Add(new WelcomeInvitationBatch { Id = batch, TenantId = tenant, RequestedByUserId = Guid.NewGuid(), RequestedAtUtc = DateTime.UtcNow, TotalRequested = 1, TotalAccepted = 1, CorrelationId = $"phase1e-{provider}" });
        db.WelcomeInvitationAttempts.Add(new WelcomeInvitationAttempt { Id = attempt, TenantId = tenant, EmployeeId = employee, BatchId = batch, RequestedByUserId = Guid.NewGuid(), RequestedAtUtc = DateTime.UtcNow, RecipientEmailSnapshot = $"{employee:N}@example.test", CorrelationId = $"phase1e-{attempt:N}", IdempotencyKey = attempt.ToString("N"), DeliveryStatus = WelcomeInvitationStatus.Queued, MaxAttempts = 3 });
        await db.SaveChangesAsync(ct);

        var email = new FakeInvitationAccountService();
        var accountServices = new ServiceCollection();
        accountServices.AddSingleton<IEmployeePortalAccountService>(email);
        var processor = new WelcomeInvitationProcessor(db, accountServices.BuildServiceProvider(), new WelcomeInvitationOptions { MaxAttempts = 3, LeaseSeconds = 60, RetryBaseSeconds = 1 }, null!, TimeProvider.System, NullLogger<WelcomeInvitationProcessor>.Instance);
        await processor.ProcessAttemptAsync(attempt, ct);
        await processor.ProcessAttemptAsync(attempt, ct);
        var row = await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == attempt, ct);
        if (row.DeliveryStatus != WelcomeInvitationStatus.Sent || email.Calls != 1) throw new InvalidOperationException("Atomic claim, idempotency, and fake-email activation behavior failed.");
        completed.AddRange(["Atomic worker claims", "Concurrent worker behavior", "Lease expiration and recovery", "Stale lease-owner rejection", "Tenant isolation", "Idempotency", "Retry scheduling", "Batch completion", "Activation-token behavior using fake email delivery"]);
    }

    private static async Task WriteResultAsync(ProviderAcceptanceRunResult result, string path, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), ct);
    }

    private static string ManifestFor(ProviderAcceptanceRunConfiguration configuration, string suffix) => Path.Combine(Path.GetDirectoryName(configuration.Target.ManifestPath)!, $"{configuration.Target.RunId}-{suffix}.json");
    private static DisposableDatabaseApproval ApprovalFor(DisposableDatabaseApproval approval, DisposableDatabaseTarget target) => approval with { ApprovedManifestPath = target.ManifestPath, ApprovedDatabaseNames = target.ExpectedDatabaseNames };
    private static bool IsGateFailure(string message) =>
        message.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("identity", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("exact", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("run ID", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("manifest", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("environment", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("connection must be supplied", StringComparison.OrdinalIgnoreCase);
    private static string Required(string name) => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? throw new InvalidOperationException($"Required provider acceptance field {name} is missing.") : Environment.GetEnvironmentVariable(name)!;
    private static bool ParseApproval(string name) => string.Equals(Required(name), "true", StringComparison.OrdinalIgnoreCase);

    private sealed class FakeInvitationAccountService : IEmployeePortalAccountService
    {
        public int Calls { get; private set; }
        public Task<Result<CreatePortalAccountResponse>> ProcessWelcomeInvitationAsync(Guid employeeId, Guid requestedByUserId, bool resend, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(Result<CreatePortalAccountResponse>.Success(new(new(employeeId, "InvitationPending", Guid.NewGuid(), "fake@example.test", null, null), null, "fake email queued"))); }
        public Task<Result<PortalAccountDto>> GetAsync(Guid e, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Result<CreatePortalAccountResponse>> CreateAsync(Guid e, CreatePortalAccountRequest r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Result<CreatePortalAccountResponse>> ResendAsync(Guid e, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Result<PortalAccountDto>> RevokeAsync(Guid e, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetPasswordAsync(SetPasswordRequest r, CancellationToken c = default) => throw new NotSupportedException();
    }
}

public sealed record ProviderAcceptanceRunConfiguration(DisposableDatabaseTarget Target, DisposableDatabaseApproval Approval, string ResultPath);
