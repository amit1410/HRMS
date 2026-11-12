using HRMS.Application.Abstractions;
using HRMS.API.Controllers;
using Microsoft.Extensions.Configuration;

namespace HRMS.EmployeeImportPreflight;

public sealed record ImportWorkerConfiguration(
    bool Enabled,
    string? TenantCode,
    Guid? OperationId,
    bool WelcomeInvitationsEnabled,
    bool LeaveRemindersEnabled,
    bool ConfirmationRemindersEnabled,
    bool LeaveAccrualEnabled,
    bool AttendanceDeviceWorkerEnabled);

public sealed record ReadinessProcess(
    int ProcessId,
    long? ProcessStartIdentity = null,
    bool? WorkerEnabled = null,
    bool AttestationValid = false,
    bool AttestationAvailable = false,
    string? AttestationDiagnostic = null,
    string? ListenerDiscoveryDiagnostic = null,
    string? ListenerDiscoveryDetail = null);

public sealed record ReadinessCheck(string Name, bool Passed, string Category);

public static class DeploymentReadinessRules
{
    public static readonly Guid TargetOperationId = Guid.Parse("b89d04aa-1419-4c85-a7e3-48ab1d92d111");
    public const string TargetTenantCode = "WEB1";
    public const string TargetDatabase = "hrms_web1";
    public static readonly Guid TargetTenantId = Guid.Parse("16313e68-2e72-4a6a-b689-79b271cec295");
    public const string TargetHost = "web1.localhost";
    public const string TargetShardKey = "web1";

    public static IReadOnlyList<ReadinessCheck> EvaluateWorkerConfiguration(ImportWorkerConfiguration value)
    {
        var exactTarget = string.Equals(value.TenantCode, TargetTenantCode, StringComparison.Ordinal)
            && value.OperationId == TargetOperationId;

        return
        [
            new("import_worker_enabled", value.Enabled, value.Enabled ? "worker.enabled" : "worker.disabled"),
            new("import_target_exact", exactTarget, exactTarget ? "worker.target_exact" : "worker.target_mismatch"),
            new("welcome_invitations_disabled", !value.WelcomeInvitationsEnabled, value.WelcomeInvitationsEnabled ? "worker.unrelated_enabled" : "worker.disabled"),
            new("leave_reminders_disabled", !value.LeaveRemindersEnabled, value.LeaveRemindersEnabled ? "worker.unrelated_enabled" : "worker.disabled"),
            new("confirmation_reminders_disabled", !value.ConfirmationRemindersEnabled, value.ConfirmationRemindersEnabled ? "worker.unrelated_enabled" : "worker.disabled"),
            new("leave_accrual_disabled", !value.LeaveAccrualEnabled, value.LeaveAccrualEnabled ? "worker.unrelated_enabled" : "worker.disabled"),
            new("attendance_device_disabled", !value.AttendanceDeviceWorkerEnabled, value.AttendanceDeviceWorkerEnabled ? "worker.unrelated_enabled" : "worker.disabled")
        ];
    }

    public static IReadOnlyList<ReadinessCheck> EvaluateNormalApiConfiguration(ImportWorkerConfiguration value)
    {
        var options = new EmployeeBulkImportWorkerOptions
        {
            Enabled = value.Enabled,
            TargetTenantCode = value.TenantCode,
            TargetOperationId = value.OperationId
        };

        return
        [
            new("normal_api_import_worker_disabled", !value.Enabled,
                value.Enabled ? "worker.enabled_in_normal_api" : "worker.disabled_in_normal_api"),
            new("normal_api_import_worker_configuration_valid", options.Validate() is null,
                options.Validate() is null ? "worker.configuration_valid" : "worker.configuration_invalid")
        ];
    }

    public static string SafeDiagnosticExceptionCategory(Exception exception) => exception switch
    {
        System.Data.Common.DbException => "database.connection_failed",
        UnauthorizedAccessException => "diagnostic.access_denied",
        TimeoutException or OperationCanceledException => "diagnostic.timeout",
        FileNotFoundException or DirectoryNotFoundException => "diagnostic.dependency_missing",
        ArgumentException or FormatException => "diagnostic.invalid_input",
        InvalidOperationException => "diagnostic.invalid_state",
        _ => "diagnostic.unexpected_exception"
    };

    public static ReadinessCheck EvaluateProcessInventory(bool inventoryAvailable, IReadOnlyList<ReadinessProcess> processes)
    {
        if (processes.Any(process => !process.AttestationAvailable))
            return new("no_competing_api_process", false, "process.attestation_unavailable");
        if (processes.Any(process => !process.AttestationValid || process.ProcessStartIdentity is null))
            return new("no_competing_api_process", false, "process.attestation_invalid_or_stale");
        if (processes.Any(process => process.WorkerEnabled is null))
            return new("no_competing_api_process", false, "process.worker_state_unknown");
        if (!inventoryAvailable)
            return new("no_competing_api_process", false, "process.inventory_unavailable");
        if (processes.Count == 0)
            return new("no_competing_api_process", true, "process.none_found");
        return processes.Any(process => process.WorkerEnabled == true)
            ? new("no_competing_api_process", false, "process.worker_enabled")
            : new("no_competing_api_process", true, "process.workers_verified_disabled");
    }

    public static bool AttestationMatches(
        ReadinessProcess process,
        WorkerStateAttestation? response,
        string expectedChallenge,
        string? echoedChallenge)
    {
        return response is not null && response.AttestationValid
            && AttestationIdentityMatches(process, response, expectedChallenge, echoedChallenge);
    }

    public static bool AttestationIdentityMatches(
        ReadinessProcess process,
        WorkerStateAttestation? response,
        string expectedChallenge,
        string? echoedChallenge)
    {
        if (response is null || process.ProcessStartIdentity is null
            || response.ProcessId != process.ProcessId
            || response.ProcessStartIdentity != process.ProcessStartIdentity
            || !string.Equals(expectedChallenge, echoedChallenge, StringComparison.Ordinal))
            return false;
        return WorkerStateAttestationRules.IsValidChallenge(expectedChallenge);
    }

    public static ReadinessCheck EvaluateCapacity(bool verified, string? physicalDatabase, long? availableBytes)
    {
        var passed = verified && string.Equals(physicalDatabase, TargetDatabase, StringComparison.OrdinalIgnoreCase)
            && availableBytes is > 0;
        return new("mysql_data_directory_capacity", passed,
            passed ? "capacity.verified" : "capacity.unavailable_or_insufficient");
    }

    public static IReadOnlyList<string> ConfigurationPrecedence(bool development, bool mountedConfigurationLoaded)
    {
        var providers = new List<string>
        {
            "appsettings.json",
            "appsettings.{Environment}.json"
        };
        if (development) providers.Add("UserSecrets");
        providers.Add("EnvironmentVariables");
        providers.Add("CommandLine");
        if (mountedConfigurationLoaded)
            providers.Add("EmployeeImportStorage:ConfigurationFile (last; overrides prior providers)");
        return providers;
    }

    public static ImportWorkerConfiguration ReadWorkerConfiguration(IConfiguration configuration)
    {
        var import = configuration.GetSection(EmployeeBulkImportWorkerOptions.SectionName)
            .Get<EmployeeBulkImportWorkerOptions>() ?? new EmployeeBulkImportWorkerOptions();
        return new(
            import.Enabled,
            import.TargetTenantCode,
            import.TargetOperationId,
            configuration.GetValue<bool>("WelcomeInvitations:Enabled", defaultValue: true),
            configuration.GetValue<bool>("LeaveReminders:Enabled"),
            configuration.GetValue<bool>("ConfirmationReminders:Enabled"),
            configuration.GetValue<bool>("LeaveAccrual:Enabled"),
            configuration.GetValue<bool>("AttendanceDeviceWorker:Enabled"));
    }
}
