using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HRMS.API;
using HRMS.API.Security;
using HRMS.Application;
using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using HRMS.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;

namespace HRMS.EmployeeImportPreflight;

public static class DeploymentReadinessRunner
{
    private const string ApprovalPhrase = "READ-ONLY WEB1 b89d04aa-1419-4c85-a7e3-48ab1d92d111";

    public static async Task<int> RunAsync(string apiDirectory, bool normalApiCandidate = false)
    {
        var checks = new List<ReadinessCheck>();
        var pids = new List<ReadinessProcess>();
        string? databaseName = null;
        string? dataDirectory = null;
        string? storageHostLocality = null;
        long? freeBytes = null;
        long? totalBytes = null;
        string? tenantCode = null;
        string? host = null;
        string? shardKey = null;
        string? provider = null;
        Guid? tenantId = null;
        ImportWorkerConfiguration? worker = null;
        string? environmentName = null;
        IReadOnlyList<string> precedence = Array.Empty<string>();
        var diagnosticStage = "approval_input";
        object? diagnosticFailure = null;

        try
        {
            if (Console.IsInputRedirected)
            {
                checks.Add(new("approval", false, "approval.interactive_input_required"));
                return PrintResult();
            }

            Console.WriteLine("Enter the read-only WEB1 deployment-readiness approval phrase:");
            checks.Add(new("approval", string.Equals(Console.ReadLine(), ApprovalPhrase, StringComparison.Ordinal), "approval.checked"));
            if (!checks[^1].Passed) return PrintResult();

            diagnosticStage = "configuration_builder";
            var webBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = "HRMS.API",
                ContentRootPath = apiDirectory,
                Args = []
            });
            var configuration = webBuilder.Configuration;
            var environment = webBuilder.Environment;
            environmentName = environment.EnvironmentName;

            var mountedConfiguration = configuration["EmployeeImportStorage:ConfigurationFile"];
            if (!string.IsNullOrWhiteSpace(mountedConfiguration))
            {
                if (!Path.IsPathFullyQualified(mountedConfiguration))
                {
                    checks.Add(new("configuration_precedence", false, "configuration.mounted_path_invalid"));
                    return PrintResult();
                }

                // Match HRMS.API: the mounted file is appended after the default providers and wins.
                configuration.AddJsonFile(mountedConfiguration, optional: false, reloadOnChange: false);
            }

            precedence = DeploymentReadinessRules.ConfigurationPrecedence(
                environment.IsDevelopment(), !string.IsNullOrWhiteSpace(mountedConfiguration));
            checks.Add(new("configuration_precedence", true, "configuration.api_order_applied"));

            diagnosticStage = "worker_configuration";
            worker = DeploymentReadinessRules.ReadWorkerConfiguration(configuration);
            checks.AddRange(normalApiCandidate
                ? DeploymentReadinessRules.EvaluateNormalApiConfiguration(worker)
                : DeploymentReadinessRules.EvaluateWorkerConfiguration(worker));
            var workerOptions = configuration.GetSection(EmployeeBulkImportWorkerOptions.SectionName)
                .Get<EmployeeBulkImportWorkerOptions>() ?? new EmployeeBulkImportWorkerOptions();
            checks.Add(new("worker_configuration_valid", workerOptions.Validate() is null,
                workerOptions.Validate() is null ? "worker.configuration_valid" : "worker.configuration_invalid"));

            try
            {
                _ = EmployeeImportStorageOptions.LoadAndValidate(configuration, environment);
                checks.Add(new("import_storage_configuration", true, "storage.configuration_valid"));
            }
            catch (Exception)
            {
                checks.Add(new("import_storage_configuration", false, "storage.configuration_invalid"));
            }

            diagnosticStage = "process_inventory";
            var processInventory = InspectLocalApiProcesses();
            if (processInventory.Processes.Count == 0)
                pids.Clear();
            else if (Console.IsInputRedirected)
                pids.AddRange(processInventory.Processes.Select(process => new ReadinessProcess(
                    process.ProcessId, process.ProcessStartIdentity,
                    AttestationDiagnostic: process.ListenerDiscoveryDiagnostic,
                    ListenerDiscoveryDiagnostic: process.ListenerDiscoveryDiagnostic,
                    ListenerDiscoveryDetail: process.ListenerDiscoveryDetail)));
            else
            {
                Console.Write("Existing WEB1 Employee.Import bearer token for live process attestation (input hidden): ");
                var token = ReadSecret();
                if (string.IsNullOrWhiteSpace(token))
                {
                    pids.AddRange(processInventory.Processes.Select(process => new ReadinessProcess(
                        process.ProcessId, process.ProcessStartIdentity,
                        AttestationDiagnostic: process.ListenerDiscoveryDiagnostic,
                        ListenerDiscoveryDiagnostic: process.ListenerDiscoveryDiagnostic,
                        ListenerDiscoveryDetail: process.ListenerDiscoveryDetail)));
                    checks.Add(new("api_process_attestation", false, "process.attestation_authorization_unavailable"));
                }
                else
                {
                    try { pids.AddRange(await AttestProcessesAsync(processInventory.Processes, token)); }
                    finally { token = string.Empty; }
                }
            }
            checks.Add(new("api_process_inventory_complete", processInventory.Available,
                processInventory.Available ? "process.inventory_complete" : "process.inventory_unavailable"));
            checks.Add(DeploymentReadinessRules.EvaluateProcessInventory(processInventory.Available, pids));

            diagnosticStage = "mysql_runtime_configuration";
            if (!string.Equals(configuration["Database:Provider"], "MySql", StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(new("web1_route_and_database", false, "database.provider_not_mysql"));
                checks.Add(DeploymentReadinessRules.EvaluateCapacity(false, null, null));
                return PrintResult();
            }

            var catalogConnectionConfigured = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Catalog"));
            var shardTemplate = configuration["Sharding:MySqlConnectionStringTemplate"];
            var shardTemplateConfigured = !string.IsNullOrWhiteSpace(shardTemplate)
                && shardTemplate.Contains("{shardKey}", StringComparison.Ordinal);
            if (!catalogConnectionConfigured || !shardTemplateConfigured)
            {
                checks.Add(new("mysql_runtime_configuration", false, "database.runtime_configuration_missing_or_invalid"));
                checks.Add(new("web1_route_and_database", false, "database.identity_unverified"));
                checks.Add(DeploymentReadinessRules.EvaluateCapacity(false, null, null));
                return PrintResult();
            }
            checks.Add(new("mysql_runtime_configuration", true, "database.runtime_configuration_available"));

            diagnosticStage = "service_registration";
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IHostEnvironment>(environment);
            services.AddApplication();
            services.AddHttpContextAccessor();
            services.AddScoped<HttpTenantContext>();
            services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<HttpTenantContext>());
            services.AddScoped<ITenantExecutionContext>(provider => provider.GetRequiredService<HttpTenantContext>());
            // Registrations do not start BackgroundService instances. This diagnostic never builds or starts an IHost.
            services.AddInfrastructure(configuration, environment, includeDeliveryInfrastructure: false);
            using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            await using var catalogScope = root.CreateAsyncScope();
            var catalog = catalogScope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();
            diagnosticStage = "catalog_route_query";
            var catalogMatches = await catalog.Tenants.AsNoTracking()
                .Where(tenant => tenant.Host == DeploymentReadinessRules.TargetHost)
                .Take(2)
                .Select(tenant => new
                {
                    tenant.Id,
                    tenant.TenantCode,
                    tenant.Host,
                    tenant.ShardKey,
                    tenant.Status,
                    tenant.DatabaseProvider
                })
                .ToListAsync();

            var resolver = catalogScope.ServiceProvider.GetRequiredService<ITenantShardResolver>();
            diagnosticStage = "tenant_route_resolution";
            var resolved = await resolver.ResolveByHostAsync(DeploymentReadinessRules.TargetHost);
            var routePassed = catalogMatches.Count == 1 && resolved is not null
                && catalogMatches[0].Id == DeploymentReadinessRules.TargetTenantId
                && catalogMatches[0].TenantCode == DeploymentReadinessRules.TargetTenantCode
                && catalogMatches[0].Host == DeploymentReadinessRules.TargetHost
                && catalogMatches[0].ShardKey == DeploymentReadinessRules.TargetShardKey
                && catalogMatches[0].Status == TenantStatus.Active
                && catalogMatches[0].DatabaseProvider == DatabaseProviderType.MySql
                && resolved.TenantId == catalogMatches[0].Id
                && resolved.TenantCode == catalogMatches[0].TenantCode
                && resolved.Host == catalogMatches[0].Host
                && resolved.ShardKey == catalogMatches[0].ShardKey
                && resolved.Status == catalogMatches[0].Status
                && resolved.DatabaseProvider == catalogMatches[0].DatabaseProvider;

            if (routePassed)
            {
                tenantId = resolved!.TenantId;
                tenantCode = resolved.TenantCode;
                host = resolved.Host;
                shardKey = resolved.ShardKey;
                provider = resolved.DatabaseProvider.ToString();

                await using var tenantScope = root.CreateAsyncScope();
                tenantScope.ServiceProvider.GetRequiredService<IShardContext>().Use(resolved);
                tenantScope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().Use(resolved.TenantId);
                diagnosticStage = "tenant_database_identity";
                var tenantDb = tenantScope.ServiceProvider.GetRequiredService<HrmsDbContext>();
                await tenantDb.Database.OpenConnectionAsync();
                await using var command = tenantDb.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT DATABASE(), @@datadir";
                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    databaseName = reader.IsDBNull(0) ? null : reader.GetString(0);
                    dataDirectory = reader.IsDBNull(1) ? null : reader.GetString(1);
                }

                var connectionBuilder = new MySqlConnectionStringBuilder(tenantDb.Database.GetDbConnection().ConnectionString);
                var isLocalServer = IsLoopback(connectionBuilder.Server);
                storageHostLocality = isLocalServer ? "local" : "remote_or_unverified";
                diagnosticStage = "storage_capacity";
                if (isLocalServer && !string.IsNullOrWhiteSpace(dataDirectory))
                    TryReadDriveCapacity(dataDirectory, out freeBytes, out totalBytes);
            }

            var databasePassed = routePassed && string.Equals(databaseName,
                DeploymentReadinessRules.TargetDatabase, StringComparison.OrdinalIgnoreCase);
            checks.Add(new("web1_route_and_database", databasePassed,
                databasePassed ? "database.identity_verified" : routePassed ? "database.physical_name_mismatch" : "catalog.route_mismatch"));
            checks.Add(DeploymentReadinessRules.EvaluateCapacity(databasePassed && storageHostLocality == "local",
                databaseName, freeBytes));
        }
        catch (Exception exception)
        {
            diagnosticFailure = new
            {
                Stage = diagnosticStage,
                ExceptionCategory = DeploymentReadinessRules.SafeDiagnosticExceptionCategory(exception)
            };
            checks.Add(new("diagnostic_execution", false, "diagnostic.read_only_check_failed"));
        }

        return PrintResult();

        int PrintResult()
        {
            var passed = checks.Count > 0 && checks.All(check => check.Passed);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Result = passed ? "PASS" : "BLOCKED",
                Environment = environmentName,
                ConfigurationProviderPrecedence = precedence,
                DatabaseIdentity = new
                {
                    Verified = checks.Any(check => check.Name == "web1_route_and_database" && check.Passed),
                    TenantId = tenantId,
                    TenantCode = tenantCode,
                    Host = host,
                    ShardKey = shardKey,
                    Provider = provider,
                    PhysicalDatabase = databaseName
                },
                Worker = worker,
                ApiProcesses = pids.Select(process => new
                {
                    process.ProcessId,
                    process.ProcessStartIdentity,
                    WorkerEnabled = process.WorkerEnabled,
                    process.AttestationValid,
                    process.AttestationDiagnostic,
                    process.ListenerDiscoveryDiagnostic,
                    process.ListenerDiscoveryDetail
                }),
                MySqlStorage = new
                {
                    Verified = freeBytes.HasValue,
                    ServerLocality = storageHostLocality,
                    DataDirectoryAvailableFreeBytes = freeBytes,
                    DataDirectoryTotalBytes = totalBytes
                },
                DiagnosticFailure = diagnosticFailure,
                Checks = checks
            }));
            return passed ? 0 : 2;
        }
    }

    private static bool IsLoopback(string server) => server.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || server.Equals("127.0.0.1", StringComparison.Ordinal)
        || server.Equals("::1", StringComparison.Ordinal);

    private static async Task<IReadOnlyList<ReadinessProcess>> AttestProcessesAsync(
        IReadOnlyList<DiscoveredApiProcess> discovered,
        string bearerToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        var targets = discovered.Select(process => new ApiProcessForAttestation(
            process.ProcessId, process.ProcessStartIdentity, process.ListeningPorts,
            process.ListenerDiscoveryDiagnostic)).ToArray();
        return await WorkerStateAttestationProbe.AttestProcessesAsync(targets, bearerToken, client);
    }

    private static void TryReadDriveCapacity(string dataDirectory, out long? freeBytes, out long? totalBytes)
    {
        freeBytes = null;
        totalBytes = null;
        try
        {
            var root = Path.GetPathRoot(dataDirectory);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(dataDirectory)) return;
            var drive = DriveInfo.GetDrives().FirstOrDefault(candidate => candidate.IsReady
                && string.Equals(candidate.RootDirectory.FullName, root, StringComparison.OrdinalIgnoreCase));
            if (drive is null) return;
            freeBytes = drive.AvailableFreeSpace;
            totalBytes = drive.TotalSize;
        }
        catch (Exception)
        {
            freeBytes = null;
            totalBytes = null;
        }
    }

    private static ProcessInventory InspectLocalApiProcesses()
    {
        ProcessInventory? latest = null;
        for (var attempt = 0; attempt < ListenerInventoryRetryPolicy.MaximumAttempts; attempt++)
        {
            latest = InspectLocalApiProcessesOnce();
            if (!ListenerInventoryRetryPolicy.ShouldRetry(
                    latest.Available,
                    latest.Processes.Select(process => process.ListenerDiscoveryDiagnostic),
                    attempt))
                return latest;

            Thread.Sleep(ListenerInventoryRetryPolicy.RetryDelayMilliseconds);
        }

        return latest ?? new(false, []);
    }

    private static ProcessInventory InspectLocalApiProcessesOnce()
    {
        var processes = new Dictionary<int, DiscoveredApiProcess>();
        foreach (var process in Process.GetProcessesByName("HRMS.API"))
        {
            try { processes[process.Id] = new(process.Id, "HRMS.API.exe", GetStartIdentity(process), []); }
            catch (Exception) { processes[process.Id] = new(process.Id, "HRMS.API.exe", null, []); }
            finally { process.Dispose(); }
        }
        if (!OperatingSystem.IsWindows()) return new(false, processes.Values.ToArray());

        Process? scanner = null;
        try
        {
            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var powershell = Path.GetFullPath(Path.Combine(systemRoot, @"WindowsPowerShell\v1.0\powershell.exe"));
            if (!File.Exists(powershell)) return new(false, processes.Values.ToArray());

            // CommandLine is inspected in memory only to identify dotnet-hosted API processes. The child
            // emits only PID, process name, and listener ports.
            const string script = @"
$ErrorActionPreference = 'Stop'
try {
  try { $allProcesses = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop) }
  catch {
    $rootException = $_.Exception
    while ($null -ne $rootException.InnerException) { $rootException = $rootException.InnerException }
    $exceptionType = [string]$rootException.GetType().Name
    if ($exceptionType -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') { $exceptionType = 'OtherException' }
    $hresult = '0x{0:X8}' -f [int]$rootException.HResult
    if ([string]$_.FullyQualifiedErrorId -match '(?i)HRESULT\s+(0x[0-9A-F]{8})') { $hresult = $Matches[1].ToUpperInvariant() }
    $category = [string]$_.CategoryInfo.Category
    if ($category -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') { $category = 'Other' }
    [Console]::Error.WriteLine(('stage=process_inventory;exception={0};hresult={1};category={2}' -f $exceptionType, $hresult, $category))
    exit 23
  }
  if ($allProcesses.Count -lt 1) {
    [Console]::Error.WriteLine('stage=process_inventory;result=empty')
    exit 23
  }
  $cimState = 'available'
  $allCimRecords = @()
  $cimExceptionType = ''
  $cimExceptionHResult = ''
  $cimExceptionCategory = ''
  try {
    $allCimRecords = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.State -eq 'Listen' })
  } catch {
    if ([string]$_.FullyQualifiedErrorId -like 'CmdletizationQuery_NotFound*') { $cimState = 'empty' }
    else { $cimState = 'failed' }
    $cimExceptionType = [string]$_.Exception.GetType().Name
    if ($cimExceptionType -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') { $cimExceptionType = 'OtherException' }
    $cimExceptionHResult = '0x{0:X8}' -f [int]$_.Exception.HResult
    $cimExceptionCategory = [string]$_.CategoryInfo.Category
    if ($cimExceptionCategory -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') { $cimExceptionCategory = 'Other' }
  }
  $netstat = Join-Path $env:SystemRoot 'System32\netstat.exe'
  try { $netstatOutput = @(& $netstat -ano -p tcp 2>&1) }
  catch {
    $netstatOutput = @()
    $netstatExitCode = -1
    $netstatErrorCategory = 'process_start_failed'
  }
  if (-not $netstatErrorCategory) { $netstatExitCode = $LASTEXITCODE }
  $netstatSucceeded = ($netstatExitCode -eq 0)
  if (-not $netstatErrorCategory -and -not $netstatSucceeded) {
    $netstatErrorText = ($netstatOutput | ForEach-Object { [string]$_ }) -join ' '
    if ($netstatErrorText -match '(?i)access is denied|access denied') { $netstatErrorCategory = 'access_denied' }
    elseif ($netstatErrorText -match '(?i)not recognized|cannot find|not found') { $netstatErrorCategory = 'executable_unavailable' }
    elseif ($netstatErrorText -match '(?i)invalid|parameter|syntax') { $netstatErrorCategory = 'invocation_invalid' }
    else { $netstatErrorCategory = 'native_command_failed' }
  }
  $netstatLines = @($netstatOutput | ForEach-Object { [string]$_ })
  $allNetstatRecords = @()
  if ($netstatSucceeded) {
    $allNetstatRecords = @($netstatLines | ForEach-Object {
      if ($_ -match '^\s*TCP\s+\S+:(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$') {
        [pscustomobject]@{ LocalPort = [int]$Matches[1]; OwningProcess = [int]$Matches[2] }
      }
    })
  }
  foreach ($process in $allProcesses) {
    $name = [string]$process.Name
    $line = [string]$process.CommandLine
    $isApi = ($name -ieq 'HRMS.API.exe') -or
             ($line -match '(?i)HRMS\.API\.dll') -or
             (($name -ieq 'dotnet.exe') -and [string]::IsNullOrWhiteSpace($line)) -or
             ($name -ieq 'w3wp.exe') -or
             ($name -ieq 'iisexpress.exe')
    if ($isApi) {
      $processId = [int]$process.ProcessId
      $processCimRecords = @($allCimRecords | Where-Object { $_.OwningProcess -eq $processId })
      $processNetstatRecords = @($allNetstatRecords | Where-Object { $_.OwningProcess -eq $processId })
      $cimPorts = @($processCimRecords | Select-Object -ExpandProperty LocalPort -Unique)
      $netstatPorts = @($processNetstatRecords | Select-Object -ExpandProperty LocalPort -Unique)

      if ($cimState -eq 'failed') {
        $ownedPorts = if ($netstatSucceeded) { $netstatPorts } else { @() }
        $ambiguousNetstatPorts = @($ownedPorts | Where-Object {
          $candidatePort = $_
          @($allNetstatRecords | Where-Object LocalPort -eq $candidatePort | Select-Object -ExpandProperty OwningProcess -Unique).Count -gt 1
        })
        if ($ambiguousNetstatPorts.Count -gt 0) {
          $ownedPorts = @($ownedPorts | Where-Object { $_ -notin $ambiguousNetstatPorts })
          $listenerSource = 'listener.owner_ambiguous'
          $listenerDetail = 'stage=netstat_fallback;result=owner_ambiguous'
        }
        elseif ($netstatSucceeded -and $ownedPorts.Count -gt 0) {
          $listenerSource = 'listener.netstat_fallback'
          $listenerDetail = 'stage=netstat_fallback;source=netstat;cimException={0};hresult={1};category={2};queryHandled=1' -f $cimExceptionType, $cimExceptionHResult, $cimExceptionCategory
        }
        elseif ($netstatSucceeded) {
          $listenerSource = 'listener.no_ports_found'
          $listenerDetail = 'stage=netstat_fallback;source=netstat;result=no_listener;cimException={0};category={1}' -f $cimExceptionType, $cimExceptionCategory
        }
        else {
          $listenerSource = 'listener.query_failed'
          $listenerDetail = 'stage=cim;exception={0};hresult={1};category={2};netstat=failed;queryHandled=1' -f $cimExceptionType, $cimExceptionHResult, $cimExceptionCategory
        }
      }
      elseif (-not $netstatSucceeded) {
        $ownedPorts = $cimPorts
        $listenerSource = if ($cimPorts.Count -gt 0) { 'listener.netstat_query_failed' } else { 'listener.query_failed' }
        $listenerDetail = 'stage=netstat;exit={0};cimState={1};error={2}' -f $netstatExitCode, $cimState, $netstatErrorCategory
      }
      else {
        $ownedPorts = @($cimPorts + $netstatPorts | Sort-Object -Unique)
        $ambiguousPorts = @($ownedPorts | Where-Object {
          $candidatePort = $_
          @($allCimRecords | Where-Object LocalPort -eq $candidatePort | Select-Object -ExpandProperty OwningProcess -Unique).Count -gt 1 -or
          @($allNetstatRecords | Where-Object LocalPort -eq $candidatePort | Select-Object -ExpandProperty OwningProcess -Unique).Count -gt 1
        })
        if ($ambiguousPorts.Count -gt 0) {
          $ownedPorts = @($ownedPorts | Where-Object { $_ -notin $ambiguousPorts })
          $listenerSource = 'listener.owner_ambiguous'
          $listenerDetail = 'stage=snapshot;result=owner_ambiguous'
        }
        $sameSnapshot = ($cimPorts.Count -eq $netstatPorts.Count) -and
          (@($cimPorts | Where-Object { $_ -notin $netstatPorts }).Count -eq 0)
        if ($ambiguousPorts.Count -eq 0) {
          if ($cimState -eq 'empty' -and $netstatPorts.Count -eq 0) { $listenerSource = 'listener.no_ports_found' }
          elseif ($sameSnapshot -and $ownedPorts.Count -gt 0) { $listenerSource = 'listener.verified' }
          elseif ($sameSnapshot) { $listenerSource = 'listener.no_ports_found' }
          else { $listenerSource = 'listener.snapshot_inconsistent' }
          $listenerDetail = 'stage=snapshot;cimState={0};cimPorts={1};netstatPorts={2};queryHandled=1' -f $cimState, $cimPorts.Count, $netstatPorts.Count
        }
      }
      [Console]::Out.WriteLine(('{0}|{1}|{2}|{3}|{4}' -f $processId, $name, ($ownedPorts -join ','), $listenerSource, $listenerDetail))
    }
  }
} catch {
  $rootException = $_.Exception
  while ($null -ne $rootException.InnerException) { $rootException = $rootException.InnerException }
  $exceptionType = [string]$rootException.GetType().Name
  if ($exceptionType -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') { $exceptionType = 'OtherException' }
  $hresult = '0x{0:X8}' -f [int]$rootException.HResult
  $category = [string]$_.CategoryInfo.Category
  if ($category -notmatch '^[A-Za-z][A-Za-z0-9_]{0,63}$') { $category = 'Other' }
  [Console]::Error.WriteLine(('stage=scanner;exception={0};hresult={1};category={2}' -f $exceptionType, $hresult, $category))
  exit 23
}
";
            var startInfo = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand "
                    + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Do not pass database, storage, JWT, or other application environment variables to the scanner.
            startInfo.Environment.Clear();
            CopyEnvironment("SystemRoot", startInfo);
            CopyEnvironment("WINDIR", startInfo);
            CopyEnvironment("PATH", startInfo);
            CopyEnvironment("TEMP", startInfo);
            CopyEnvironment("TMP", startInfo);
            var modulePaths = new List<string>
            {
                Path.Combine(systemRoot, @"WindowsPowerShell\v1.0\Modules")
            };
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                modulePaths.Add(Path.Combine(programFiles, "WindowsPowerShell", "Modules"));
            startInfo.Environment["PSModulePath"] = string.Join(';', modulePaths.Where(Directory.Exists));

            scanner = Process.Start(startInfo);
            if (scanner is null) return new(false, WithScannerFailure(processes.Values, "stage=scanner;result=start_failed"));
            var stdoutTask = scanner.StandardOutput.ReadToEndAsync();
            var stderrTask = scanner.StandardError.ReadToEndAsync();
            if (!scanner.WaitForExit(15_000))
            {
                try { scanner.Kill(entireProcessTree: true); }
                catch (Exception) { }
                return new(false, WithScannerFailure(processes.Values, "stage=scanner;result=timeout"));
            }

            var safeOutput = stdoutTask.GetAwaiter().GetResult();
            var safeError = stderrTask.GetAwaiter().GetResult();
            if (scanner.ExitCode != 0)
            {
                var detail = GetScannerFailureDetail(safeError, scanner.ExitCode);
                return new(false, WithScannerFailure(processes.Values, detail));
            }

            foreach (var line in safeOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split('|', 5);
                if (fields.Length != 5 || !int.TryParse(fields[0], out var pid)
                    || fields[1].Length > 100 || fields[1].Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
                    continue;
                var ports = fields[2].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => int.TryParse(value, out var port) && port is > 0 and <= 65535 ? port : 0)
                    .Where(port => port != 0).Distinct().ToArray();
                long? startIdentity = null;
                try
                {
                    using var targetProcess = Process.GetProcessById(pid);
                    startIdentity = GetStartIdentity(targetProcess);
                }
                catch (Exception) { }
                var discoveryDiagnostic = fields[3] switch
                {
                    "listener.no_ports_found" => "listener.no_ports_found",
                    "listener.query_failed" => "listener.query_failed",
                    "listener.netstat_query_failed" => "listener.netstat_query_failed",
                    "listener.snapshot_inconsistent" => "listener.snapshot_inconsistent",
                    "listener.owner_ambiguous" => "listener.owner_ambiguous",
                    "listener.verified" => "listener.verified",
                    "listener.get_net_tcp_connection" => ports.Length == 0 ? "listener.no_ports_found" : "listener.discovered",
                    "listener.netstat_fallback" => ports.Length == 0 ? "listener.fallback_no_ports_found" : "listener.discovered_via_netstat",
                    _ => "listener.discovery_failed"
                };
                var detail = fields.Length == 5 && fields[4].Length <= 180
                    && fields[4].All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '=' or ';' or '.' or '-')
                    ? fields[4] + ";scannerExit=" + scanner.ExitCode
                    : "stage=output_parse;result=detail_unavailable;scannerExit=" + scanner.ExitCode;
                processes[pid] = new DiscoveredApiProcess(pid, fields[1], startIdentity, ports, discoveryDiagnostic, detail);
            }

            return new(true, processes.Values.OrderBy(process => process.ProcessId).ToArray());
        }
        catch (Exception)
        {
            return new(false, WithScannerFailure(processes.Values, "stage=scanner;result=execution_failed"));
        }
        finally
        {
            scanner?.Dispose();
        }
    }

    private static IReadOnlyList<DiscoveredApiProcess> WithScannerFailure(
        IEnumerable<DiscoveredApiProcess> processes,
        string detail) => processes.OrderBy(process => process.ProcessId)
        .Select(process => process with
        {
            ListenerDiscoveryDiagnostic = "listener.discovery_unavailable",
            ListenerDiscoveryDetail = detail
        }).ToArray();

    private static string GetScannerFailureDetail(string error, int exitCode)
    {
        var match = System.Text.RegularExpressions.Regex.Match(error,
            @"stage=(process_inventory|scanner);exception=([A-Za-z][A-Za-z0-9_]{0,63});hresult=(0x[0-9A-Fa-f]{8});category=([A-Za-z][A-Za-z0-9_]{0,63})?");
        return match.Success
            ? $"stage={match.Groups[1].Value};exception={match.Groups[2].Value};hresult={match.Groups[3].Value};exit={exitCode}"
            : error.Contains("stage=process_inventory;result=empty", StringComparison.Ordinal)
                ? $"stage=process_inventory;result=empty;exit={exitCode}"
            : $"stage=scanner;result=nonzero_exit;exit={exitCode}";
    }

    private static void CopyEnvironment(string name, ProcessStartInfo startInfo)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value)) startInfo.Environment[name] = value;
    }

    private static string ReadSecret()
    {
        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0) characters.RemoveAt(characters.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar)) characters.Add(key.KeyChar);
        }
        Console.WriteLine();
        var value = new string(characters.ToArray());
        characters.Clear();
        return value;
    }

    private static long GetStartIdentity(Process process) =>
        process.StartTime.ToUniversalTime().ToFileTimeUtc();

    private sealed record DiscoveredApiProcess(
        int ProcessId,
        string Name,
        long? ProcessStartIdentity,
        IReadOnlyList<int> ListeningPorts,
        string ListenerDiscoveryDiagnostic = "listener.discovery_unavailable",
        string ListenerDiscoveryDetail = "stage=discovery;result=detail_unavailable");

    private sealed record ProcessInventory(bool Available, IReadOnlyList<DiscoveredApiProcess> Processes);
}
