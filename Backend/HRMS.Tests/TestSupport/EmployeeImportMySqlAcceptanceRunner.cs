using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Storage;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.EmployeeCodes;
using HRMS.Application.Services;
using HRMS.Application.Validators.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.Data.MySqlClient;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Json;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Executable provider harness. Configuration is fully gated before this class opens a connection.
/// Provisioned databases are never automatically deleted on failure; cleanup is a separate approved action.
/// </summary>
public sealed class EmployeeImportMySqlAcceptanceRunner
{
    private readonly Action? beforeFirstConnection;

    public EmployeeImportMySqlAcceptanceRunner(Action? beforeFirstConnection = null) => this.beforeFirstConnection = beforeFirstConnection;

    public async Task<EmployeeImportMySqlRunResult> RunAsync(
        IReadOnlyDictionary<string, string?>? values = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = EmployeeImportMySqlAcceptanceConfiguration.Read(
            values ?? EmployeeImportMySqlAcceptanceEnvironment.Snapshot());
        if (configuration.DatabaseMode == EmployeeImportDatabaseMode.AdoptExistingEmpty)
            throw new NotImplementedException("AdoptExistingEmpty is not ready for execution in Phase A. Phase B must implement and approve the complete read-only preflight and mutation boundary first; no resource was touched.");
        ValidateComposeConfiguration(configuration);
        ValidateStorageConfiguration(configuration);
        using var credentialScope = EmployeeImportMySqlCredentialAdapter.BeginPromptSession(configuration);
        var completed = new List<string>();
        var checkpoints = new AcceptanceCheckpointTracker(configuration.ResultPath, configuration.RunId, completed);
        (int ProcessedRows, int SuccessfulRows)? largeImport = null;
        var manifest = NewManifest(configuration);
        await EmployeeImportMySqlOwnershipManifest.WriteNewAsync(manifest, configuration.ManifestPath, cancellationToken);
        var stage = "provider target verification";
        try
        {
            // This is the first provider operation; the gate above has already validated every approval and target.
            beforeFirstConnection?.Invoke();
            await checkpoints.RunAsync("target_verified", () => VerifyTargetAsync(configuration, cancellationToken));
            stage = "run-scoped database absence verification";
            foreach (var database in manifest.Databases)
                if (await DatabaseExistsAsync(configuration, database.Name, cancellationToken))
                    throw new InvalidOperationException("Refusing to use an existing database in the disposable allowlist.");

            stage = "run-scoped database creation";
            foreach (var database in manifest.Databases)
            {
                await CreateOwnedDatabaseAsync(configuration, database.Name, database.OwnershipMarker, cancellationToken);
                manifest = Update(manifest, database.Name, created: true);
                await EmployeeImportMySqlOwnershipManifest.WriteNewOrReplaceAsync(manifest, configuration.ManifestPath, cancellationToken);
            }

            stage = "catalog migration";
            await MigrateAsync(configuration, configuration.CatalogDatabase, catalog: true, cancellationToken);
            stage = "tenant migration";
            await MigrateAsync(configuration, configuration.TenantDatabase, catalog: false, cancellationToken);
            stage = "migration-down schema migration";
            await MigrateAsync(configuration, configuration.MigrationDownDatabase, catalog: false, cancellationToken);
            manifest = manifest with { Databases = manifest.Databases.Select(x => x with { MigrationSucceeded = true }).ToArray() };
            await EmployeeImportMySqlOwnershipManifest.WriteNewOrReplaceAsync(manifest, configuration.ManifestPath, cancellationToken);
            completed.Add("Catalog and tenant MySQL migrations applied");

            stage = "employee-import schema verification";
            await VerifyImportSchemaAsync(configuration, cancellationToken);
            completed.Add("Employee import operation/row tables and tenant keys verified on MySQL");
            stage = "encrypted workbook storage verification";
            await VerifyMinioWorkbookStoreAsync(configuration, cancellationToken);
            completed.Add("S3-compatible encrypted workbook persistence, integrity, privacy, duplicate and key-rotation checks");
            stage = "employee-import worker execution";
            largeImport = await VerifyMySqlOnboardingExecutionAsync(configuration, checkpoints, cancellationToken);
            completed.Add("Durable prepare, confirm/queue, worker execution, generated code, audit association, and committed row outcomes");
            stage = "migration-down rollback";
            await MigrateDownAsync(configuration, cancellationToken);
            completed.Add("Migration-down database rolled back in isolation");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = EmployeeImportMySqlFailureDiagnostics.Create(stage, exception, checkpoints.ActiveCheckpoint);
            await WriteResultAsync(configuration.ResultPath, new(configuration.RunId, "FAILED_RETAINED_FOR_REVIEW", completed,
                "Provider acceptance failed; owned databases were retained for inspection.", failure,
                LastCompletedCheckpoint: checkpoints.LastCompletedCheckpoint,
                FailingCheckpoint: checkpoints.ActiveCheckpoint), CancellationToken.None);
            throw new InvalidOperationException("Employee-import MySQL acceptance failed. The run-owned databases were retained; inspect the private manifest and results before any cleanup.");
        }

        var result = new EmployeeImportMySqlRunResult(configuration.RunId, "SCHEMA_PROVIDER_CHECKS_PASSED", completed,
            "Employee import execution and MinIO end-to-end scenarios completed successfully.",
            Failure: null, ProcessedRows: largeImport!.Value.ProcessedRows, SuccessfulRows: largeImport.Value.SuccessfulRows,
            LastCompletedCheckpoint: checkpoints.LastCompletedCheckpoint, FailingCheckpoint: null);
        await WriteResultAsync(configuration.ResultPath, result, cancellationToken);
        return result;
    }

    public async Task CleanupAsync(IReadOnlyDictionary<string, string?>? values = null, CancellationToken cancellationToken = default)
    {
        var configuration = EmployeeImportMySqlAcceptanceConfiguration.Read(values ?? EmployeeImportMySqlAcceptanceEnvironment.Snapshot(), allowExistingManifest: true);
        using var credentialScope = EmployeeImportMySqlCredentialAdapter.BeginPromptSession(configuration);
        beforeFirstConnection?.Invoke();
        var manifestText = await File.ReadAllTextAsync(configuration.ManifestPath, cancellationToken);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<EmployeeImportMySqlOwnershipManifest>(manifestText,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The disposable ownership manifest is malformed.");
        await VerifyTargetAsync(configuration, cancellationToken);
        var observed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var db in manifest.Databases)
            observed[db.Name] = await ReadOwnershipMarkerAsync(configuration, db.Name, cancellationToken);
        manifest.ValidateCleanup(configuration, IsApproved(values ?? EmployeeImportMySqlAcceptanceEnvironment.Snapshot(), EmployeeImportMySqlAcceptanceEnvironment.ApproveDelete), observed);
        foreach (var db in manifest.Databases)
        {
            await DropDatabaseAsync(configuration, db.Name, cancellationToken);
            if (await DatabaseExistsAsync(configuration, db.Name, cancellationToken))
                throw new InvalidOperationException("Disposable database absence could not be verified after cleanup.");
        }
    }

    private static EmployeeImportMySqlOwnershipManifest NewManifest(EmployeeImportMySqlAcceptanceConfiguration c)
    {
        var names = new[] { c.CatalogDatabase, c.TenantDatabase, c.MigrationDownDatabase };
        return new(1, c.RunId, c.ServerIdentity, c.HostPort, c.ServerPort,
            names.Select(name => new EmployeeImportOwnedDatabase(name, false, false,
                $"HRMS-EMPLOYEE-IMPORT-F1|{c.ServerIdentity}|{c.RunId}|{name}")).ToArray(), DateTimeOffset.UtcNow);
    }

    private static EmployeeImportMySqlOwnershipManifest Update(EmployeeImportMySqlOwnershipManifest manifest, string name, bool created) =>
        manifest with { Databases = manifest.Databases.Select(x => x.Name == name ? x with { Created = created } : x).ToArray() };

    private static async Task VerifyTargetAsync(EmployeeImportMySqlAcceptanceConfiguration c, CancellationToken ct)
    {
        await using var connection = await OpenAdminAsync(c, ct);
        var host = Convert.ToString(await ScalarAsync(connection, "SELECT @@hostname", ct), System.Globalization.CultureInfo.InvariantCulture);
        var port = Convert.ToInt32(await ScalarAsync(connection, "SELECT @@port", ct), System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(host, c.ServerIdentity, StringComparison.Ordinal) || port != c.ServerPort)
            throw new InvalidOperationException("Observed MySQL server identity or port does not match the approved disposable target.");
    }

    private static void ValidateStorageConfiguration(EmployeeImportMySqlAcceptanceConfiguration c)
    {
        if (!File.Exists(c.StorageConfigurationPath))
            throw new InvalidOperationException("The private test-only S3/encryption configuration file is missing.");
        var config = new ConfigurationBuilder().AddJsonFile(c.StorageConfigurationPath, optional: false, reloadOnChange: false).Build();
        var options = EmployeeImportStorageOptions.LoadAndValidate(config, new AcceptanceHostEnvironment());
        if (c.StorageMode == EmployeeImportStorageMode.FileSystem)
        {
            if (options.Provider != EmployeeImportStorageProvider.FileSystem || string.IsNullOrWhiteSpace(options.RootPath))
                throw new InvalidOperationException("Filesystem acceptance requires EmployeeImportStorage:Provider=FileSystem.");
            var expectedRoot = c.FileSystemRoot;
            if (string.IsNullOrWhiteSpace(expectedRoot) || !string.Equals(Path.GetFullPath(options.RootPath), Path.GetFullPath(expectedRoot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Filesystem acceptance root must exactly match the approved run-scoped root.");
            return;
        }
        if (options.Provider != EmployeeImportStorageProvider.S3 || options.Endpoint is null ||
            !string.Equals(options.Endpoint.Host, "127.0.0.1", StringComparison.Ordinal) || options.Endpoint.Port != c.MinioPort ||
            options.UseTls || !options.AllowInsecureDevelopmentEndpoint ||
            options.CredentialMode != EmployeeImportStorageCredentialMode.Static ||
            !string.Equals(options.Bucket, $"hrms-employee-import-{c.RunId.ToLowerInvariant()}", StringComparison.Ordinal))
            throw new InvalidOperationException("Employee-import provider acceptance requires the configured private loopback MinIO endpoint in Development mode.");
        if (config.GetSection("EmployeeImportStorage:Encryption:Keys").GetChildren().Count() < 2)
            throw new InvalidOperationException("MinIO key-rotation acceptance requires at least two externally supplied test encryption keys.");
    }

    private static void ValidateComposeConfiguration(EmployeeImportMySqlAcceptanceConfiguration c)
    {
        if (c.MySqlMode == EmployeeImportMySqlMode.ExistingLoopback) return;
        if (!File.Exists(c.ComposeFile) || !string.Equals(Path.GetFileName(c.ComposeFile), "docker-compose.f1.yml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configured F1 Compose file is missing or is not the dedicated employee-import stack file.");
    }

    private static async Task<bool> DatabaseExistsAsync(EmployeeImportMySqlAcceptanceConfiguration c, string name, CancellationToken ct)
    {
        await using var connection = await OpenAdminAsync(c, ct);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @db";
        command.Parameters.AddWithValue("@db", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static async Task CreateOwnedDatabaseAsync(EmployeeImportMySqlAcceptanceConfiguration c, string name, string marker, CancellationToken ct)
    {
        ValidateIdentifier(name);
        await using var server = await OpenAdminAsync(c, ct);
        await using (var create = server.CreateCommand()) { create.CommandText = $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"; await create.ExecuteNonQueryAsync(ct); }
        await using var database = await OpenDatabaseAsync(c, name, ct);
        await using var ownership = database.CreateCommand();
        ownership.CommandText = "CREATE TABLE `__HRMS_EmployeeImportF1_Ownership` (Id INT NOT NULL PRIMARY KEY, RunId VARCHAR(48) NOT NULL, ServerIdentity VARCHAR(255) NOT NULL, DatabaseName VARCHAR(64) NOT NULL, Marker VARCHAR(512) NOT NULL); INSERT INTO `__HRMS_EmployeeImportF1_Ownership` VALUES (1,@run,@server,@database,@marker)";
        ownership.Parameters.AddWithValue("@run", c.RunId); ownership.Parameters.AddWithValue("@server", c.ServerIdentity);
        ownership.Parameters.AddWithValue("@database", name); ownership.Parameters.AddWithValue("@marker", marker);
        await ownership.ExecuteNonQueryAsync(ct);
    }

    private static async Task MigrateAsync(EmployeeImportMySqlAcceptanceConfiguration c, string name, bool catalog, CancellationToken ct)
    {
        if (catalog)
        {
            await using var context = CreateCatalogContext(c, name);
            await context.Database.MigrateAsync(ct);
        }
        else
        {
            await using var context = CreateTenantContext(c, name);
            await context.Database.MigrateAsync(ct);
        }
    }

    private static async Task VerifyImportSchemaAsync(EmployeeImportMySqlAcceptanceConfiguration c, CancellationToken ct)
    {
        await using var context = CreateTenantContext(c, c.TenantDatabase);
        if (!await context.Database.CanConnectAsync(ct)) throw new InvalidOperationException("Disposable tenant database is unavailable after migration.");
        var operation = context.Model.FindEntityType(typeof(HRMS.Domain.Entities.EmployeeImportOperation))
            ?? throw new InvalidOperationException("Employee import operation mapping is missing.");
        var row = context.Model.FindEntityType(typeof(HRMS.Domain.Entities.EmployeeImportRow))
            ?? throw new InvalidOperationException("Employee import row mapping is missing.");
        if (operation.GetIndexes().All(x => !x.IsUnique || !x.Properties.Any(p => p.Name == nameof(HRMS.Domain.Entities.EmployeeImportOperation.WorkbookSha256))) ||
            row.GetIndexes().All(x => !x.IsUnique || !x.Properties.Any(p => p.Name == nameof(HRMS.Domain.Entities.EmployeeImportRow.ExcelRowNumber))))
            throw new InvalidOperationException("Employee import uniqueness mappings are incomplete.");
        await context.Database.ExecuteSqlRawAsync("SELECT 1 FROM EmployeeImportOperations LIMIT 0", ct);
        await context.Database.ExecuteSqlRawAsync("SELECT 1 FROM EmployeeImportRows LIMIT 0", ct);
    }

    private static async Task<(int ProcessedRows, int SuccessfulRows)> VerifyMySqlOnboardingExecutionAsync(EmployeeImportMySqlAcceptanceConfiguration c, AcceptanceCheckpointTracker checkpoints, CancellationToken ct)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var shard = new Tenant
        {
            Id = tenantId, TenantCode = $"F1{tenantId:N}"[..12], TenantName = "Employee import F1 synthetic tenant",
            Host = $"{tenantId:N}.f1.invalid", ShardKey = c.TenantDatabase,
            DatabaseProvider = DatabaseProviderType.MySql, Status = TenantStatus.Active
        };
        await using (var catalog = CreateCatalogContext(c, c.CatalogDatabase))
        {
            catalog.Tenants.Add(shard);
            await catalog.SaveChangesAsync(ct);
        }
        var tenant = new TestTenantContext(tenantId, userId);
        await using (var db = CreateTenantContext(c, c.TenantDatabase))
        {
            await DatabaseSeeder.SeedShardAsync(db, new IdentityPasswordHasher(), shard, ct,
                configuration: new ConfigurationBuilder().Build(), isDevelopment: false);
            var seededUser = await db.Users.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.Id).FirstAsync(ct);
            tenant.UserId = seededUser;
            var authorization = new AcceptanceAuthorization(Permissions.Employee.Import, Permissions.Employee.Create,
                Permissions.EmployeeSensitive.Edit, Permissions.EmploymentHistory.Change);
            var clock = TimeProvider.System;
            var codeService = new EmployeeCodeConfigurationService(db, tenant, new EmployeeCodeConfigurationRequestValidator());
            var codeConfig = await codeService.SaveAsync(new EmployeeCodeConfigurationRequest
            {
                AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto, GenerationMethod = EmployeeCodeGenerationMethod.Simple,
                Prefix = "F1", Separator = "-", NextNumber = 1, Padding = 5, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow)
            }, ct);
            if (!codeConfig.Succeeded) throw new InvalidOperationException("Synthetic employee-code configuration could not be established.");

            var jobStatus = new JobStatusService(db, tenant, authorization, clock);
            var employment = new EmployeeEmploymentService(db, tenant, authorization, Microsoft.Extensions.Logging.Abstractions.NullLogger<EmployeeEmploymentService>.Instance,
                new EmployeeCodeRuleMatcher(), new EmployeeCodeRenderer(), new EmployeeCodeSequenceService(db, tenant), clock, jobStatusService: jobStatus);
            var employeeService = new EmployeeService(db, tenant, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<EmployeeService>.Instance,
                jobStatusService: jobStatus, employmentService: employment, authorization: authorization);
            var validation = new EmployeeBulkUploadValidationService(db, tenant, new EmployeePersonalDetailsRequestValidator(),
                new EmployeeContactRequestValidator(), new EmploymentChangeRequestValidator());
            var persistence = new EmployeeImportPersistenceService(db, tenant, clock);
            var storeConfig = new ConfigurationBuilder().AddJsonFile(c.StorageConfigurationPath, optional: false, reloadOnChange: false).Build();
            var storageOptions = EmployeeImportStorageOptions.LoadAndValidate(storeConfig, new AcceptanceHostEnvironment());
            var workerContext = new EmployeeImportWorkerExecutionContext();
            IEmployeeImportWorkbookStore workbookStore = c.StorageMode == EmployeeImportStorageMode.FileSystem
                ? new EncryptedFileEmployeeImportWorkbookStore(storeConfig, new AcceptanceHostEnvironment(), tenant, clock,
                    new EmployeeImportWorkbookCipher(storeConfig))
                : CreateS3Store(storeConfig, storageOptions, tenant, clock);
            var execution = new EmployeeBulkImportExecutionService(db, tenant, validation, persistence, workbookStore, employeeService, workerContext);
            var template = await new EmployeeBulkUploadTemplateService(db, tenant).GenerateAsync(ct);
            if (!template.Succeeded) throw new InvalidOperationException("Synthetic workbook template generation failed.");
            using var workbook = new XLWorkbook(new MemoryStream(template.Value!));
            var sheet = workbook.Worksheet("Employees");
            var columns = sheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
            var designation = await db.Designations.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => x.Code).FirstAsync(ct);
            var department = await db.Departments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => x.Code).FirstAsync(ct);
            var initialNames = new[] { "SyntheticF1A", "SyntheticF1B", "SyntheticF1C" };
            AddSyntheticRows(sheet, columns, department, designation, initialNames, RandomNumberGenerator.GetInt32(0, 10000));
            var sensitiveIdentifiers = initialNames.SelectMany((_, index) => new[]
            {
                sheet.Cell(index + 2, columns["Aadhaar Number *"]).GetString(),
                sheet.Cell(index + 2, columns["PAN *"]).GetString()
            }).ToArray();
            using var stream = new MemoryStream(); workbook.SaveAs(stream); var bytes = stream.ToArray();
            var before = await db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId, ct);
            using var input = new MemoryStream(bytes, writable: false);
            var prepared = await execution.PrepareAsync(input, "synthetic-f1.xlsx", ct);
            if (!prepared.Succeeded) throw new InvalidOperationException("Synthetic employee import preparation failed validation.");
            if (await db.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId, ct) != before)
                throw new InvalidOperationException("Preparation created an employee before confirmation.");
            var shardDescriptor = new ShardDescriptor(shard.Id, shard.TenantCode, shard.Host, shard.ShardKey, shard.Status, shard.DatabaseProvider);
            await ConfirmOverHttpAsync(c, shardDescriptor, tenantId, seededUser, prepared.Value!, ct);
            var confirmedState = await ReadOperationStateAsync(c, tenantId, prepared.Value.OperationId, ct);
            if (confirmedState != EmployeeImportOperationState.Queued)
                throw new InvalidOperationException("HTTP confirmation did not durably queue the import before worker startup.");
            await using (var afterConfirmation = CreateTenantContext(c, c.TenantDatabase))
                if (await afterConfirmation.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId, ct) != before)
                    throw new InvalidOperationException("HTTP 202 confirmation created employees before the isolated worker started.");

            // First isolated API host simulates a process interruption after row 2 commits and before row 3 starts.
            var interruptAtNextRow = new PauseAtRowStartInterceptor(3);
            using (var interruptedHost = new EmployeeImportF1ApiFactory(c, workerEnabled: true,
                       shard.TenantCode, prepared.Value.OperationId, interruptAtNextRow))
            using (var interruptedClient = interruptedHost.CreateClientFor(shard.Host))
            {
                interruptedClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(seededUser, tenantId, shard.TenantCode,
                    permissions: [Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit]));
                await checkpoints.RunAsync("interruption_row2_committed", () => WaitForImportAsync(c, tenantId, prepared.Value.OperationId,
                    async (context, operation) => operation.State == EmployeeImportOperationState.Processing &&
                        await context.EmployeeImportRows.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId && x.OperationId == operation.Id &&
                            x.ExcelRowNumber == 2 && x.Status == EmployeeImportRowStatus.Succeeded, ct), ct));
                // Stop the isolated host while the worker is paused before row 3. Throwing from
                // SaveChanges would be treated as a recoverable row failure by the worker and
                // would not simulate a process interruption or preserve the processing lease.
                await checkpoints.RunAsync("interruption_row3_paused", () => interruptAtNextRow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(45), ct));
                await checkpoints.RunAsync("interruption_host_shutdown_requested", () => interruptedHost.StopHostAsync(ct));
                await checkpoints.RunAsync("interruption_cancellation_observed", () => interruptAtNextRow.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10), ct));
            }

            Guid oldLeaseId;
            await using (var afterInterruption = CreateTenantContext(c, c.TenantDatabase))
            {
                await checkpoints.BeginAsync("interruption_lease_retained");
                var interrupted = await afterInterruption.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Value.OperationId, ct);
                if (interrupted.State != EmployeeImportOperationState.Processing || interrupted.ClaimLeaseId is null)
                    throw new InvalidOperationException("Interrupted import did not retain a recoverable processing lease.");
                oldLeaseId = interrupted.ClaimLeaseId.Value;
                await checkpoints.CompleteAsync();
                await checkpoints.RunAsync("interruption_lease_expired", () => ExpireLeaseAsync(c, tenantId, interrupted.Id, ct));
            }

            var pauseAtRecovery = new PauseAtRowStartInterceptor(3);
            using var recoveryHost = new EmployeeImportF1ApiFactory(c, workerEnabled: true,
                shard.TenantCode, prepared.Value.OperationId, pauseAtRecovery);
            using var recoveryClient = recoveryHost.CreateClientFor(shard.Host);
            recoveryClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(seededUser, tenantId, shard.TenantCode,
                permissions: [Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit]));
            await checkpoints.RunAsync("recovery_worker_paused", () => pauseAtRecovery.Entered.Task.WaitAsync(TimeSpan.FromSeconds(45), ct));
            using var concurrentHost = new EmployeeImportF1ApiFactory(c, workerEnabled: true,
                shard.TenantCode, prepared.Value.OperationId);
            using var concurrentClient = concurrentHost.CreateClientFor(shard.Host);
            concurrentClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(seededUser, tenantId, shard.TenantCode,
                permissions: [Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit]));

            await using var fencingDb = CreateTenantContext(c, c.TenantDatabase);
            var stalePersistence = new EmployeeImportPersistenceService(fencingDb, tenant, clock);
            await checkpoints.BeginAsync("recovery_worker_claimed_new_lease");
            var currentLease = await fencingDb.EmployeeImportOperations.IgnoreQueryFilters().Where(x => x.Id == prepared.Value.OperationId)
                .Select(x => x.ClaimLeaseId).SingleAsync(ct);
            if (currentLease is null || currentLease == oldLeaseId)
                throw new InvalidOperationException("Expired lease was not replaced by recovery worker ownership.");
            await checkpoints.CompleteAsync();
            await checkpoints.RunAsync("stale_worker_renewal_rejected", async () =>
            {
                var staleRenewal = await stalePersistence.RenewClaimAsync(prepared.Value.OperationId, oldLeaseId, ct);
                if (staleRenewal.Succeeded) throw new InvalidOperationException("Stale worker was able to renew a reclaimed lease.");
            });
            var existingEmployee = await fencingDb.Employees.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.FirstName == initialNames[0])
                .Select(x => new { x.Id, x.EmployeeCode }).SingleAsync(ct);
            await using (var staleTransaction = await fencingDb.Database.BeginTransactionAsync(ct))
            {
                var staleOutcome = await stalePersistence.CompleteRowWithinTransactionAsync(prepared.Value.OperationId, 3,
                    existingEmployee.Id, existingEmployee.EmployeeCode, oldLeaseId, staleTransaction, ct);
                await staleTransaction.RollbackAsync(ct);
                if (staleOutcome.Succeeded) throw new InvalidOperationException("Stale worker committed a row outcome after lease ownership changed.");
            }

            pauseAtRecovery.Release.TrySetResult();
            await checkpoints.RunAsync("recovery_final_completion", () => WaitForImportAsync(c, tenantId, prepared.Value.OperationId,
                (_, operation) => Task.FromResult(operation.State == EmployeeImportOperationState.Completed), ct));
            var status = await execution.GetStatusAsync(prepared.Value.OperationId, cancellationToken: ct);
            if (!status.Succeeded || status.Value!.State != "Completed" || status.Value.SucceededRows != 3 ||
                status.Value.Rows.Select(x => x.EmployeeCode).Distinct(StringComparer.Ordinal).Count() != 3)
                throw new InvalidOperationException("Recovered hosted worker did not complete each employee exactly once with unique generated codes.");
            var publicStatus = System.Text.Json.JsonSerializer.Serialize(status.Value);
            if (sensitiveIdentifiers.Any(value => publicStatus.Contains(value, StringComparison.Ordinal)))
                throw new InvalidOperationException("Sensitive synthetic identifiers leaked into the import status response.");
            var importedEmployees = await db.Employees.IgnoreQueryFilters().Where(x => initialNames.Contains(x.FirstName)).ToListAsync(ct);
            if (importedEmployees.Count != initialNames.Length || importedEmployees.Any(x => x.TenantId != tenantId || string.IsNullOrWhiteSpace(x.EmployeeCode)))
                throw new InvalidOperationException("Hosted worker did not persist every employee under the catalog-selected tenant shard with a generated code.");
            var audits = await db.EmployeeAuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.ImportBatchId == prepared.Value.OperationId).CountAsync(ct);
            if (audits < 3) throw new InvalidOperationException("Employee import audit association was not persisted for each successful row.");
            db.ClearChangeTracker();
            var operation = await db.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Value.OperationId, ct);
            if (operation.SucceededRows != 3 || operation.FailedRows != 0 || operation.SkippedRows != 0)
                throw new InvalidOperationException("MySQL durable row outcome counts are inconsistent after recovery.");
            var history = await execution.GetHistoryAsync(1, 50, ct);
            if (!history.Succeeded || history.Value!.TotalCount == 0 || history.Value.Items.All(x => x.OperationId != prepared.Value.OperationId))
                throw new InvalidOperationException("Completed import is missing from tenant history.");

            var httpStatus = await recoveryClient.GetAsync($"/api/employees/bulk-upload/{prepared.Value.OperationId:D}", ct);
            var httpStatusBody = await httpStatus.Content.ReadAsStringAsync(ct);
            if (!httpStatus.IsSuccessStatusCode || !httpStatusBody.Contains("Completed", StringComparison.Ordinal) ||
                sensitiveIdentifiers.Any(value => httpStatusBody.Contains(value, StringComparison.Ordinal)))
                throw new InvalidOperationException("Tenant-scoped status API failed or exposed a synthetic sensitive identifier.");
            var httpHistory = await recoveryClient.GetAsync("/api/employees/bulk-upload/history?page=1&pageSize=20", ct);
            if (!httpHistory.IsSuccessStatusCode) throw new InvalidOperationException("Tenant-scoped import history API failed.");
            using (var foreignTenantClient = recoveryHost.CreateClientFor(shard.Host))
            {
                foreignTenantClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                    TestTokens.Create(Guid.NewGuid(), Guid.NewGuid(), "FOREIGN_F1",
                        permissions: [Permissions.Employee.Import]));
                var foreignTenantResponse = await foreignTenantClient.GetAsync($"/api/employees/bulk-upload/{prepared.Value.OperationId:D}", ct);
                if (foreignTenantResponse.IsSuccessStatusCode)
                    throw new InvalidOperationException("A foreign tenant token read the run-owned import status through HTTP.");
            }

            await using (var otherTenantDb = CreateTenantContext(c, c.TenantDatabase))
            {
                var otherTenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
                var otherPersistence = new EmployeeImportPersistenceService(otherTenantDb, otherTenant, clock);
                var crossTenantRead = await otherPersistence.GetAsync(prepared.Value.OperationId, ct);
                if (crossTenantRead.Succeeded) throw new InvalidOperationException("A different tenant resolved this import operation.");
            }
            recoveryHost.Dispose();
            concurrentHost.Dispose();

            var largeImport = await VerifyTenThousandRowScenarioAsync(c, db, execution, shardDescriptor, tenantId, seededUser,
                shard, tenant, template.Value!, department, designation, ct);

            // A runtime failure after a prior row committed must retain that success and close later rows as skipped.
            var partialNames = new[] { $"SyntheticPartialOk{RandomNumberGenerator.GetInt32(1000, 9999)}", $"SyntheticPartialFail{RandomNumberGenerator.GetInt32(1000, 9999)}" };
            using var partialWorkbook = new XLWorkbook(new MemoryStream(template.Value!));
            var partialSheet = partialWorkbook.Worksheet("Employees");
            var partialColumns = partialSheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
            AddSyntheticRows(partialSheet, partialColumns, department, designation, partialNames, RandomNumberGenerator.GetInt32(0, 10000));
            using var partialStream = new MemoryStream(); partialWorkbook.SaveAs(partialStream);
            using var partialInput = new MemoryStream(partialStream.ToArray(), writable: false);
            var partialPrepared = await execution.PrepareAsync(partialInput, "synthetic-partial-f1.xlsx", ct);
            if (!partialPrepared.Succeeded) throw new InvalidOperationException("Synthetic partial-failure workbook did not validate.");
            await ConfirmOverHttpAsync(c, shardDescriptor, tenantId, seededUser, partialPrepared.Value!, ct);
            using (var partialHost = new EmployeeImportF1ApiFactory(c, workerEnabled: true,
                       shard.TenantCode, partialPrepared.Value.OperationId, new FailEmployeeSaveInterceptor(partialNames[1])))
            using (var partialClient = partialHost.CreateClientFor(shard.Host))
            {
                partialClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(seededUser, tenantId, shard.TenantCode,
                    permissions: [Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit]));
                await WaitForImportAsync(c, tenantId, partialPrepared.Value.OperationId,
                    (_, operation) => Task.FromResult(operation.State == EmployeeImportOperationState.PartiallyCompleted), ct);
                var partialStatus = await partialClient.GetAsync($"/api/employees/bulk-upload/{partialPrepared.Value.OperationId:D}", ct);
                var partialBody = await partialStatus.Content.ReadAsStringAsync(ct);
                if (!partialStatus.IsSuccessStatusCode || !partialBody.Contains("PartiallyCompleted", StringComparison.Ordinal) ||
                    !partialBody.Contains("SucceededRows\":1", StringComparison.OrdinalIgnoreCase) ||
                    !partialBody.Contains("FailedRows\":1", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Partial runtime failure did not produce accurate sanitized status counts.");
                await using var partialDb = CreateTenantContext(c, c.TenantDatabase);
                var partialOperation = await partialDb.EmployeeImportOperations.IgnoreQueryFilters()
                    .SingleAsync(x => x.TenantId == tenantId && x.Id == partialPrepared.Value.OperationId, ct);
                if (partialOperation.SucceededRows != 1 || partialOperation.FailedRows != 1 || partialOperation.SkippedRows != 0)
                    throw new InvalidOperationException("Partial import success/failure/skipped counts are not durable and exact.");
            }

            // Fresh DbContext simulates process restart after the row transaction committed.
            await using (var restartedDb = CreateTenantContext(c, c.TenantDatabase))
            {
                var afterRestart = await restartedDb.Employees.IgnoreQueryFilters()
                    .CountAsync(x => x.TenantId == tenantId && initialNames.Contains(x.FirstName), ct);
                var restartedOperation = await restartedDb.EmployeeImportOperations.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == prepared.Value.OperationId, ct);
                if (afterRestart != initialNames.Length || restartedOperation.SucceededRows != initialNames.Length)
                    throw new InvalidOperationException("Committed employees and row-success outcomes were not durable across a fresh context.");
            }

            // Two independent MySQL connections racing for one queued operation must produce one owner.
            var extraTokenHash = Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray()));
            var extraWorkbookHash = Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray()));
            var extra = await persistence.BeginPreparationAsync(extraWorkbookHash, extraTokenHash, 1, Guid.NewGuid(), "claim-race.xlsx", ct);
            if (!extra.Succeeded) throw new InvalidOperationException("Could not create the synthetic concurrent-claim operation.");
            var prepLease = extra.Value!.ClaimLeaseId ?? throw new InvalidOperationException("Preparation claim was not persisted.");
            if (!(await persistence.CompletePreparationAsync(extra.Value.Id, prepLease, "Synthetic claim race", ct)).Succeeded ||
                !(await persistence.EnqueueAsync(extra.Value.Id, extraTokenHash, ct)).Succeeded)
                throw new InvalidOperationException("Synthetic claim-race operation could not be queued.");
            await using var claimDbA = CreateTenantContext(c, c.TenantDatabase);
            await using var claimDbB = CreateTenantContext(c, c.TenantDatabase);
            var claimA = new EmployeeImportPersistenceService(claimDbA, tenant, clock)
                .ClaimQueuedOperationAsync(extra.Value.Id, Guid.NewGuid(), ct);
            var claimB = new EmployeeImportPersistenceService(claimDbB, tenant, clock)
                .ClaimQueuedOperationAsync(extra.Value.Id, Guid.NewGuid(), ct);
            var claims = await Task.WhenAll(claimA, claimB);
            if (claims.Count(x => x.Succeeded && x.Value is not null) != 1)
                throw new InvalidOperationException("Concurrent MySQL workers did not produce exactly one claim owner.");
            // The unique operation/object identity was created by this run; deletion uses the store's retention guard.
            await workbookStore.DeleteExpiredAsync(prepared.Value.OperationId, DateTime.UtcNow.AddYears(-20), ct);
            CryptographicOperations.ZeroMemory(bytes);
            return largeImport;
        }
    }

    private static async Task MigrateDownAsync(EmployeeImportMySqlAcceptanceConfiguration c, CancellationToken ct)
    {
        await using var context = CreateTenantContext(c, c.MigrationDownDatabase);
        var migrations = (await context.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if (migrations.Length == 0) throw new InvalidOperationException("Migration-down verification database has no applied migrations.");
        await context.GetService<IMigrator>().MigrateAsync((string?)null, ct);
        if ((await context.Database.GetAppliedMigrationsAsync(ct)).Any()) throw new InvalidOperationException("Migration-down verification left applied tenant migrations.");
    }

    private static async Task VerifyMinioWorkbookStoreAsync(EmployeeImportMySqlAcceptanceConfiguration c, CancellationToken ct)
    {
        var config = new ConfigurationBuilder().AddJsonFile(c.StorageConfigurationPath, optional: false, reloadOnChange: false).Build();
        var environment = new AcceptanceHostEnvironment();
        var options = EmployeeImportStorageOptions.LoadAndValidate(config, environment);
        if (c.StorageMode == EmployeeImportStorageMode.FileSystem)
        {
            await VerifyFileSystemWorkbookStoreAsync(c, config, environment, ct);
            return;
        }
        if (options.Provider != EmployeeImportStorageProvider.S3 || options.Endpoint is null ||
            !string.Equals(options.Endpoint.Host, "127.0.0.1", StringComparison.Ordinal) || options.Endpoint.Port != c.MinioPort ||
            options.UseTls || !options.AllowInsecureDevelopmentEndpoint)
            throw new InvalidOperationException("Employee-import provider acceptance requires the configured private loopback MinIO endpoint in Development mode.");

        var aws = new AmazonS3Config
        {
            ServiceURL = options.Endpoint.ToString().TrimEnd('/'), AuthenticationRegion = options.Region,
            ForcePathStyle = true, Timeout = TimeSpan.FromSeconds(15), MaxErrorRetry = 0
        };
        var credentials = StaticAcceptanceCredentials(options);
        using var client = new AmazonS3Client(credentials, aws);
        using var anonymous = new AmazonS3Client(new AnonymousAWSCredentials(), aws);
        var tenantId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var tenant = new TestTenantContext(tenantId, Guid.NewGuid());
        var clock = TimeProvider.System;
        var cipher = new EmployeeImportWorkbookCipher(config);
        var store = new S3EmployeeImportWorkbookStore(config, environment, tenant, clock, cipher, new S3EmployeeImportObjectClient(client));
        var workbookBytes = CreateSyntheticWorkbook();
        var hash = Convert.ToHexString(SHA256.HashData(workbookBytes));
        try
        {
            await using var input = new MemoryStream(workbookBytes, writable: false);
            await store.SaveAsync(operationId, input, hash, ct);
            await RestartOwnedMinioContainerAsync(c, ct);
            Assert(await store.IsAvailableAsync(operationId, ct), "Uploaded private MinIO object was not found.");
            await AssertThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var repeated = new MemoryStream(workbookBytes, writable: false);
                await store.SaveAsync(operationId, repeated, hash, ct);
            }, "Duplicate object upload was not rejected.");

            await using (var download = await store.OpenReadAsync(operationId, hash, ct) ?? throw new InvalidOperationException("Encrypted workbook download returned no object."))
            using (var output = new MemoryStream())
            {
                await download.CopyToAsync(output, ct);
                Assert(SHA256.HashData(output.ToArray()).AsSpan().SequenceEqual(Convert.FromHexString(hash)), "Downloaded workbook SHA-256 differs from uploaded content.");
            }
            await AssertThrowsAsync<InvalidDataException>(async () => { await using var _ = await store.OpenReadAsync(operationId, new string('0', 64), ct); }, "Mismatched workbook hash was accepted.");

            using (var raw = await client.GetObjectAsync(options.Bucket, ObjectKey(tenantId, operationId), ct))
            using (var encryptedStream = new MemoryStream())
            {
                await raw.ResponseStream.CopyToAsync(encryptedStream, ct);
                var envelope = encryptedStream.ToArray();
                var keys = config.GetSection("EmployeeImportStorage:Encryption:Keys").GetChildren().ToArray();
                var initialKey = config["EmployeeImportStorage:Encryption:ActiveKeyId"];
                var alternate = keys.FirstOrDefault(x => !string.Equals(x.Key, initialKey, StringComparison.Ordinal));
                if (alternate is null) throw new InvalidOperationException("Key-rotation acceptance requires at least two externally supplied persistent test keys.");
                var rotatedSettings = config.AsEnumerable().ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                rotatedSettings["EmployeeImportStorage:Encryption:ActiveKeyId"] = alternate.Key;
                var rotatedConfiguration = new ConfigurationBuilder().AddInMemoryCollection(rotatedSettings).Build();
                var rotatedCipher = new EmployeeImportWorkbookCipher(rotatedConfiguration);
                var recovered = rotatedCipher.Decrypt(tenantId, operationId, envelope);
                Assert(SHA256.HashData(recovered).AsSpan().SequenceEqual(Convert.FromHexString(hash)), "Previous-key decryption failed after active-key rotation.");
                CryptographicOperations.ZeroMemory(recovered);

                envelope[^1] ^= 0x01;
                using var tampered = new MemoryStream(envelope, writable: false);
                await client.PutObjectAsync(new PutObjectRequest { BucketName = options.Bucket, Key = ObjectKey(tenantId, operationId), InputStream = tampered, ContentType = "application/octet-stream" }, ct);
                await AssertThrowsAsync<InvalidDataException>(async () => { await using var _ = await store.OpenReadAsync(operationId, hash, ct); }, "Tampered MinIO ciphertext was accepted.");
            }

            var otherTenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
            var otherStore = new S3EmployeeImportWorkbookStore(config, environment, otherTenant, clock, cipher, new S3EmployeeImportObjectClient(client));
            Assert(await otherStore.OpenReadAsync(operationId, hash, ct) is null, "Cross-tenant object lookup returned another tenant's artifact.");
            try
            {
                using var anonymousRead = await anonymous.GetObjectAsync(options.Bucket, ObjectKey(tenantId, operationId), ct);
                throw new InvalidOperationException("Anonymous access to the private workbook bucket succeeded.");
            }
            catch (AmazonS3Exception) { }

            // New store/client objects simulate an application restart while retaining the same external bucket and key ring.
            using var restartedClient = new AmazonS3Client(credentials, aws);
            var restartedStore = new S3EmployeeImportWorkbookStore(config, environment, tenant, clock, cipher, new S3EmployeeImportObjectClient(restartedClient));
            Assert(await restartedStore.IsAvailableAsync(operationId, ct), "Workbook object did not survive client restart.");
        }
        finally { CryptographicOperations.ZeroMemory(workbookBytes); }
    }

    private static async Task VerifyFileSystemWorkbookStoreAsync(EmployeeImportMySqlAcceptanceConfiguration c,
        IConfiguration config, IHostEnvironment environment, CancellationToken ct)
    {
        var options = EmployeeImportStorageOptions.LoadAndValidate(config, environment);
        if (options.Provider != EmployeeImportStorageProvider.FileSystem || string.IsNullOrWhiteSpace(options.RootPath))
            throw new InvalidOperationException("Filesystem acceptance requires the encrypted filesystem workbook store.");
        var tenantId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var tenant = new TestTenantContext(tenantId, Guid.NewGuid());
        var store = new EncryptedFileEmployeeImportWorkbookStore(config, environment, tenant, TimeProvider.System,
            new EmployeeImportWorkbookCipher(config));
        var workbookBytes = CreateSyntheticWorkbook();
        var hash = Convert.ToHexString(SHA256.HashData(workbookBytes));
        try
        {
            await using var input = new MemoryStream(workbookBytes, writable: false);
            await store.SaveAsync(operationId, input, hash, ct);
            Assert(await store.IsAvailableAsync(operationId, ct), "Encrypted filesystem workbook was not persisted.");
            await using var output = await store.OpenReadAsync(operationId, hash, ct) ?? throw new InvalidOperationException("Encrypted filesystem workbook could not be read.");
            using var restored = new MemoryStream();
            await output.CopyToAsync(restored, ct);
            Assert(SHA256.HashData(restored.ToArray()).AsSpan().SequenceEqual(Convert.FromHexString(hash)), "Filesystem workbook hash changed after round-trip.");
            var otherTenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
            var otherStore = new EncryptedFileEmployeeImportWorkbookStore(config, environment, otherTenant, TimeProvider.System,
                new EmployeeImportWorkbookCipher(config));
            Assert(await otherStore.OpenReadAsync(operationId, hash, ct) is null, "Filesystem workbook lookup crossed tenant scope.");
        }
        finally { CryptographicOperations.ZeroMemory(workbookBytes); }
    }

    private static IEmployeeImportWorkbookStore CreateS3Store(IConfiguration config, EmployeeImportStorageOptions options,
        ITenantContext tenant, TimeProvider clock)
    {
        var s3Config = new AmazonS3Config { ServiceURL = options.Endpoint!.ToString().TrimEnd('/'), AuthenticationRegion = options.Region, ForcePathStyle = true, Timeout = TimeSpan.FromSeconds(15), MaxErrorRetry = 0 };
        var client = new AmazonS3Client(StaticAcceptanceCredentials(options), s3Config);
        return new S3EmployeeImportWorkbookStore(config, new AcceptanceHostEnvironment(), tenant, clock,
            new EmployeeImportWorkbookCipher(config), new S3EmployeeImportObjectClient(client));
    }

    private static byte[] CreateSyntheticWorkbook()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Employees").Cell("A1").Value = "Synthetic acceptance row";
        using var output = new MemoryStream(); workbook.SaveAs(output); return output.ToArray();
    }

    private static string ObjectKey(Guid tenant, Guid operation) => $"employee-import-workbooks/{tenant:N}/{operation:N}.hrw";

    private static async Task RestartOwnedMinioContainerAsync(EmployeeImportMySqlAcceptanceConfiguration c, CancellationToken ct)
    {
        var ids = await RunDockerAsync(["ps", "--filter", $"label=com.hrms.run-id={c.RunId}", "--filter", "label=com.hrms.owner=employee-bulk-import-f1",
            "--filter", "label=com.hrms.disposable=true", "--filter", "label=com.docker.compose.service=minio", "--filter", $"label=com.docker.compose.project={c.ComposeProject}", "--format", "{{.ID}}"], ct);
        var idList = ids.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (idList.Length != 1) throw new InvalidOperationException("MinIO restart refused: exactly one run-owned MinIO container was not found.");

        var labelsJson = await RunDockerAsync(["inspect", "--format", "{{json .Config.Labels}}", idList[0]], ct);
        using var labels = System.Text.Json.JsonDocument.Parse(labelsJson);
        var root = labels.RootElement;
        string Label(string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
        if (Label("com.hrms.disposable") != "true" || Label("com.hrms.owner") != "employee-bulk-import-f1" ||
            Label("com.hrms.run-id") != c.RunId || Label("com.docker.compose.project") != c.ComposeProject ||
            Label("com.docker.compose.service") != "minio")
            throw new InvalidOperationException("MinIO restart refused because container ownership labels did not match the approved run.");

        _ = await RunDockerAsync(["restart", "--time", "20", idList[0]], ct);
        var running = await RunDockerAsync(["inspect", "--format", "{{.State.Running}}", idList[0]], ct);
        if (!string.Equals(running.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The owned MinIO container did not return to running state after restart.");
    }

    private static async Task<string> RunDockerAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new System.Diagnostics.ProcessStartInfo("docker")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new System.Diagnostics.Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Docker CLI could not be started for the approved MinIO restart check.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            _ = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("Docker ownership check or controlled MinIO restart failed.");
            return output.Trim();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Docker CLI is unavailable; MinIO restart acceptance cannot run.");
        }
    }

    private static void AddSyntheticRows(IXLWorksheet sheet, IReadOnlyDictionary<string, int> columns, string departmentCode, string designationCode, IReadOnlyList<string> firstNames, int uniqueSeed)
    {
        var today = DateTime.UtcNow.Date;
        for (var index = 0; index < firstNames.Count; index++)
        {
            var row = index + 2;
            void Text(string header, string value) => sheet.Cell(row, columns[header]).SetValue(value);
            Text("First Name *", firstNames[index]);
            Text("Last Name *", "Acceptance");
            Text("Aadhaar Number *", $"9{uniqueSeed:00000}{index + 1:000000}");
            Text("PAN *", $"SYNTH{(uniqueSeed + index) % 10000:0000}A");
            sheet.Cell(row, columns["Date of Joining *"]).SetValue(today);
            Text("Job Status *", "On Probation");
            sheet.Cell(row, columns["Employment Effective From *"]).SetValue(today);
            Text("Department Code *", departmentCode);
            Text("Designation Code *", designationCode);
            Text("Employment Change Reason *", "NewJoining");
            Text("Employment Type *", "FullTime");
        }
    }

    private static async Task ConfirmOverHttpAsync(EmployeeImportMySqlAcceptanceConfiguration c, ShardDescriptor shard,
        Guid tenantId, Guid userId, EmployeeBulkImportPreparationDto prepared, CancellationToken cancellationToken)
    {
        using var host = new EmployeeImportF1ApiFactory(c, workerEnabled: false);
        using var client = host.CreateClientFor(shard.Host);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            TestTokens.Create(userId, tenantId, shard.TenantCode,
                permissions: [Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit]));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await client.PostAsJsonAsync("/api/employees/bulk-upload/confirm",
            new { operationId = prepared.OperationId, confirmationToken = prepared.ConfirmationToken, confirm = true }, timeout.Token);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException("The real API confirmation endpoint did not return HTTP 202 Accepted.");
        using var replay = await client.PostAsJsonAsync("/api/employees/bulk-upload/confirm",
            new { operationId = prepared.OperationId, confirmationToken = prepared.ConfirmationToken, confirm = true }, timeout.Token);
        if (replay.StatusCode == HttpStatusCode.Accepted)
            throw new InvalidOperationException("The real API accepted a replayed one-use confirmation token.");
    }

    private static async Task<EmployeeImportOperationState> ReadOperationStateAsync(EmployeeImportMySqlAcceptanceConfiguration c,
        Guid tenantId, Guid operationId, CancellationToken cancellationToken)
    {
        await using var context = CreateTenantContext(c, c.TenantDatabase);
        return await context.EmployeeImportOperations.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Id == operationId).Select(x => x.State).SingleAsync(cancellationToken);
    }

    private static async Task WaitForImportAsync(EmployeeImportMySqlAcceptanceConfiguration c, Guid tenantId, Guid operationId,
        Func<HrmsDbContext, EmployeeImportOperation, Task<bool>> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        while (!timeout.IsCancellationRequested)
        {
            await using var context = CreateTenantContext(c, c.TenantDatabase);
            var operation = await context.EmployeeImportOperations.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, timeout.Token);
            if (operation is not null && await condition(context, operation)) return;
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
        throw new TimeoutException("The isolated employee-import worker did not reach the expected deterministic state within 45 seconds.");
    }

    private static async Task ExpireLeaseAsync(EmployeeImportMySqlAcceptanceConfiguration c, Guid tenantId, Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var context = CreateTenantContext(c, c.TenantDatabase);
        var changed = await context.EmployeeImportOperations.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.Id == operationId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ClaimLeaseExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)), cancellationToken);
        if (changed != 1) throw new InvalidOperationException("Could not expire exactly the run-owned operation lease for recovery testing.");
    }

    private sealed class PauseAtRowStartInterceptor(int excelRow) : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var isTarget = eventData.Context?.ChangeTracker.Entries<EmployeeImportRow>().Any(entry =>
                entry.Entity.ExcelRowNumber == excelRow && entry.Entity.Status == EmployeeImportRowStatus.Processing) == true;
            if (isTarget)
            {
                Entered.TrySetResult();
                try
                {
                    await Release.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    CancellationObserved.TrySetResult();
                    throw;
                }
            }
            return result;
        }
    }

    private sealed class AcceptanceCheckpointTracker(string resultPath, string runId, List<string> completedChecks)
    {
        public string? ActiveCheckpoint { get; private set; }
        public string? LastCompletedCheckpoint { get; private set; }

        public async Task RunAsync(string checkpoint, Func<Task> action)
        {
            await BeginAsync(checkpoint);
            await action();
            await CompleteAsync();
        }

        public Task BeginAsync(string checkpoint)
        {
            if (string.IsNullOrWhiteSpace(checkpoint) || checkpoint.Length > 80 ||
                checkpoint.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
                throw new ArgumentException("Checkpoint identifiers must be sanitized ASCII identifiers.", nameof(checkpoint));
            ActiveCheckpoint = checkpoint;
            return PersistAsync();
        }

        public async Task CompleteAsync()
        {
            if (ActiveCheckpoint is null) throw new InvalidOperationException("No active acceptance checkpoint exists.");
            LastCompletedCheckpoint = ActiveCheckpoint;
            ActiveCheckpoint = null;
            await PersistAsync();
        }

        private Task PersistAsync() => WriteResultAsync(resultPath,
            new EmployeeImportMySqlRunResult(runId, "RUNNING", completedChecks,
                "Provider acceptance is in progress; owned databases remain run-scoped.", null,
                LastCompletedCheckpoint: LastCompletedCheckpoint,
                FailingCheckpoint: ActiveCheckpoint), CancellationToken.None);
    }

    private static async Task<(int ProcessedRows, int SuccessfulRows)> VerifyTenThousandRowScenarioAsync(
        EmployeeImportMySqlAcceptanceConfiguration c,
        HrmsDbContext db,
        IEmployeeBulkImportExecutionService execution,
        ShardDescriptor shardDescriptor,
        Guid tenantId,
        Guid seededUser,
        Tenant shard,
        TestTenantContext tenant,
        byte[] templateBytes,
        string department,
        string designation,
        CancellationToken ct)
    {
        const int totalRows = 10_000;
        var firstNames = Enumerable.Range(1, totalRows).Select(index => $"Synthetic10K{index:00000}").ToArray();
        var failedNames = new[] { firstNames[0], firstNames[4_998], firstNames[9_998] };
        using var workbook = new XLWorkbook(new MemoryStream(templateBytes));
        var sheet = workbook.Worksheet("Employees");
        var columns = sheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        AddSyntheticRows(sheet, columns, department, designation, firstNames, 10_000);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        using var input = new MemoryStream(stream.ToArray(), writable: false);
        var prepared = await execution.PrepareAsync(input, "synthetic-10000-f1.xlsx", ct);
        if (!prepared.Succeeded || prepared.Value!.TotalRows != totalRows)
            throw new InvalidOperationException("The dedicated 10,000-row workbook did not prepare as exactly 10,000 valid rows.");

        await ConfirmOverHttpAsync(c, shardDescriptor, tenantId, seededUser, prepared.Value, ct);
        var failureInterceptor = new FailEmployeeSaveInterceptor(failedNames);
        using var workerHost = new EmployeeImportF1ApiFactory(c, workerEnabled: true,
            shard.TenantCode, prepared.Value.OperationId, failureInterceptor);
        using var workerClient = workerHost.CreateClientFor(shard.Host);
        workerClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", TestTokens.Create(seededUser, tenantId, shard.TenantCode,
                permissions: [Permissions.Employee.Import, Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit]));
        await WaitForImportAsync(c, tenantId, prepared.Value.OperationId,
            (_, operation) => Task.FromResult(operation.State == EmployeeImportOperationState.PartiallyCompleted), ct);

        await using var verificationDb = CreateTenantContext(c, c.TenantDatabase);
        var imported = await verificationDb.Employees.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && firstNames.Contains(x.FirstName))
            .ToListAsync(ct);
        if (imported.Count != totalRows - failedNames.Length || imported.Select(x => x.FirstName).Distinct(StringComparer.Ordinal).Count() != imported.Count ||
            imported.Select(x => x.EmployeeCode).Where(x => x is not null).Distinct(StringComparer.Ordinal).Count() != imported.Count)
            throw new InvalidOperationException("The 10,000-row import did not create each valid employee exactly once with unique generated codes.");
        if (await verificationDb.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId && failedNames.Contains(x.FirstName), ct))
            throw new InvalidOperationException("A deliberately failed employee survived its row transaction; rollback was not verified.");

        var operation = await verificationDb.EmployeeImportOperations.IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Value.OperationId, ct);
        if (operation.TotalRows != totalRows || operation.SucceededRows != totalRows - 3 || operation.FailedRows != 3 || operation.SkippedRows != 0)
            throw new InvalidOperationException("The 10,000-row operation counters are not exactly 9,997 succeeded, 3 failed, 0 skipped.");
        if (await verificationDb.EmployeeImportRows.IgnoreQueryFilters().AnyAsync(x => x.OperationId == operation.Id && x.Status == EmployeeImportRowStatus.Pending, ct))
            throw new InvalidOperationException("The completed 10,000-row import left pending rows.");

        var statusPages = new List<EmployeeBulkImportRowResultDto>();
        for (var page = 1; page <= 10; page++)
        {
            using var response = await workerClient.GetAsync($"/api/employees/bulk-upload/{operation.Id:D}?resultPage={page}&resultPageSize=1000", ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"10,000-row status page {page} was not retrievable.");
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeBulkImportStatusDto>>(cancellationToken: ct)
                ?? throw new InvalidOperationException("10,000-row status response was empty.");
            if (envelope.Data is null || envelope.Data.ResultTotalRows != totalRows || envelope.Data.ResultTotalPages != 10 || envelope.Data.Rows.Count != 1000)
                throw new InvalidOperationException("10,000-row status pagination returned inaccurate totals.");
            statusPages.AddRange(envelope.Data.Rows);
        }
        var failedResults = statusPages.Where(x => x.Status == EmployeeImportRowStatus.Failed.ToString()).ToArray();
        if (statusPages.Count != totalRows || failedResults.Select(x => x.ExcelRowNumber).ToArray() is not [2, 5000, 10000] ||
            failedResults.Any(x => x.ErrorCode != "employee.create_failed" || string.IsNullOrWhiteSpace(x.Message) || x.Message.Contains("Synthetic", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The 10,000-row status response did not preserve the three specific sanitized failed-row outcomes.");

        using var csvResponse = await workerClient.GetAsync($"/api/employees/bulk-upload/{operation.Id:D}/failed-rows.csv", ct);
        if (!csvResponse.IsSuccessStatusCode) throw new InvalidOperationException("Failed-row CSV export was not available for the 10,000-row operation.");
        var csv = await csvResponse.Content.ReadAsStringAsync(ct);
        var csvLines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (csvLines.Length != 4 || !csvLines[0].Contains("Excel Row", StringComparison.Ordinal) ||
            !csvLines.Any(x => x.StartsWith("2,Failed,", StringComparison.Ordinal)) ||
            !csvLines.Any(x => x.StartsWith("5000,Failed,", StringComparison.Ordinal)) ||
            !csvLines.Any(x => x.StartsWith("10000,Failed,", StringComparison.Ordinal)))
            throw new InvalidOperationException("Failed-row CSV export did not contain exactly the three durable failed rows.");

        return (operation.TotalRows, operation.SucceededRows);
    }

    private sealed class FailEmployeeSaveInterceptor(params string[] firstNames) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var fail = eventData.Context?.ChangeTracker.Entries<Employee>().Any(entry =>
                entry.State == EntityState.Added && firstNames.Contains(entry.Entity.FirstName, StringComparer.Ordinal)) == true;
            if (fail) throw new InvalidOperationException("Synthetic F1 employee persistence failure.");
            return ValueTask.FromResult(result);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static AWSCredentials StaticAcceptanceCredentials(EmployeeImportStorageOptions options)
    {
        if (options.CredentialMode != EmployeeImportStorageCredentialMode.Static ||
            string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey))
            throw new InvalidOperationException("F1 requires explicit static disposable MinIO credentials; external/default credential chains are disabled.");
        return new BasicAWSCredentials(options.AccessKey, options.SecretKey);
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private static HrmsDbContext CreateTenantContext(EmployeeImportMySqlAcceptanceConfiguration c, string db) =>
        new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(Connection(c, db), x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext());
    private static HrmsCatalogDbContext CreateCatalogContext(EmployeeImportMySqlAcceptanceConfiguration c, string db) =>
        new(new DbContextOptionsBuilder<HrmsCatalogDbContext>().UseMySQL(Connection(c, db), x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlCatalogMigrations")).Options);
    private static string Connection(EmployeeImportMySqlAcceptanceConfiguration c, string db)
        => EmployeeImportMySqlCredentialAdapter.Connection(c, db);
    private static async Task<MySqlConnection> OpenAdminAsync(EmployeeImportMySqlAcceptanceConfiguration c, CancellationToken ct)
    {
        var connection = new MySqlConnection(EmployeeImportMySqlCredentialAdapter.Connection(c, "mysql"));
        await connection.OpenAsync(ct);
        return connection;
    }
    private static Task<MySqlConnection> OpenDatabaseAsync(EmployeeImportMySqlAcceptanceConfiguration c, string db, CancellationToken ct) => OpenAsync(Connection(c, db), ct);
    private static async Task<MySqlConnection> OpenAsync(string value, CancellationToken ct) { var connection = new MySqlConnection(value); await connection.OpenAsync(ct); return connection; }
    private static async Task<object?> ScalarAsync(MySqlConnection connection, string sql, CancellationToken ct) { await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(ct); }
    private static async Task<string> ReadOwnershipMarkerAsync(EmployeeImportMySqlAcceptanceConfiguration c, string db, CancellationToken ct)
    {
        await using var connection = await OpenDatabaseAsync(c, db, ct);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Marker FROM `__HRMS_EmployeeImportF1_Ownership` WHERE Id=1";
        return Convert.ToString(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
    private static async Task DropDatabaseAsync(EmployeeImportMySqlAcceptanceConfiguration c, string db, CancellationToken ct)
    {
        ValidateIdentifier(db); await using var connection = await OpenAdminAsync(c, ct);
        await using var command = connection.CreateCommand(); command.CommandText = $"DROP DATABASE `{db}`"; await command.ExecuteNonQueryAsync(ct);
    }
    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Invalid database identifier; refusing provider operation.");
    }
    private static bool IsApproved(IReadOnlyDictionary<string, string?> values, string key) => values.TryGetValue(key, out var value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    private static async Task WriteResultAsync(string path, EmployeeImportMySqlRunResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }), ct);
    }

    private sealed class AcceptanceHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "HRMS.Tests.EmployeeImportF1";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }

    private sealed class AcceptanceAuthorization(params string[] permissions) : ICurrentAuthorizationContext
    {
        private readonly HashSet<string> granted = permissions.ToHashSet(StringComparer.Ordinal);
        public bool HasAnyPermission(params string[] requested) => requested.Any(granted.Contains);
    }
}

public sealed record EmployeeImportMySqlRunResult(
    string RunId,
    string Status,
    IReadOnlyList<string> CompletedChecks,
    string Notes,
    EmployeeImportMySqlFailureDiagnostic? Failure = null,
    int? ProcessedRows = null,
    int? SuccessfulRows = null,
    string? LastCompletedCheckpoint = null,
    string? FailingCheckpoint = null);
