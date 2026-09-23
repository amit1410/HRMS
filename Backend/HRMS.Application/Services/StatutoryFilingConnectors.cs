using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;

namespace HRMS.Application.Services;

public sealed class ManualDownloadStatutoryFilingConnector : IStatutoryFilingConnector
{
    public StatutoryFilingConnectorType Type => StatutoryFilingConnectorType.ManualDownload;
    public Task<Result<string?>> ValidateConfigurationAsync(StatutoryFilingConnectionProfile? profile, CancellationToken ct = default) => Task.FromResult(Result<string?>.Success("ManualDownload requires no external credentials."));
    public Task<Result<StatutoryFilingConnectorResult>> SubmitAsync(StatutoryFilingPackage package, StatutoryFilingConnectionProfile? profile, CancellationToken ct = default) => Task.FromResult(Result<StatutoryFilingConnectorResult>.Success(new(StatutoryFilingSubmissionOutcome.Pending, null, "MANUAL_DOWNLOAD", "ManualDownload package is ready for operator download and external portal submission.", false)));
    public Task<Result<StatutoryFilingConnectorResult>> RefreshStatusAsync(StatutoryFilingSubmission submission, StatutoryFilingConnectionProfile? profile, CancellationToken ct = default) => Task.FromResult(Result<StatutoryFilingConnectorResult>.Success(new(submission.Outcome, submission.ExternalReference, "MANUAL_STATUS", "ManualDownload status is maintained by operator acknowledgement.", false)));
}

public sealed class TestStatutoryFilingConnector : IStatutoryFilingConnector
{
    public StatutoryFilingConnectorType Type => StatutoryFilingConnectorType.Test;
    public Task<Result<string?>> ValidateConfigurationAsync(StatutoryFilingConnectionProfile? profile, CancellationToken ct = default)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.Endpoint)) return Task.FromResult(Result<string?>.Invalid("endpoint", "The deterministic test connector requires a non-secret endpoint value."));
        return Task.FromResult(Result<string?>.Success("Test connector configuration is valid."));
    }

    public Task<Result<StatutoryFilingConnectorResult>> SubmitAsync(StatutoryFilingPackage package, StatutoryFilingConnectionProfile? profile, CancellationToken ct = default)
    {
        var config = profile?.NonSecretConfigurationJson ?? string.Empty;
        var outcome = config.Contains("retryable", StringComparison.OrdinalIgnoreCase) ? StatutoryFilingSubmissionOutcome.Failed : config.Contains("rejected", StringComparison.OrdinalIgnoreCase) ? StatutoryFilingSubmissionOutcome.Rejected : config.Contains("pending", StringComparison.OrdinalIgnoreCase) ? StatutoryFilingSubmissionOutcome.Pending : StatutoryFilingSubmissionOutcome.Accepted;
        var retryable = outcome == StatutoryFilingSubmissionOutcome.Failed;
        return Task.FromResult(Result<StatutoryFilingConnectorResult>.Success(new(outcome, outcome == StatutoryFilingSubmissionOutcome.Accepted ? $"TEST-{package.PackageHash[..12]}" : null, retryable ? "RETRYABLE_EXTERNAL_FAILURE" : "TEST_CONNECTOR", $"Deterministic test connector outcome: {outcome}.", retryable || outcome == StatutoryFilingSubmissionOutcome.Pending)));
    }

    public Task<Result<StatutoryFilingConnectorResult>> RefreshStatusAsync(StatutoryFilingSubmission submission, StatutoryFilingConnectionProfile? profile, CancellationToken ct = default) => Task.FromResult(Result<StatutoryFilingConnectorResult>.Success(new(submission.Outcome, submission.ExternalReference, "TEST_STATUS", "Deterministic test connector status refreshed.", false)));
}

public sealed class StatutoryFilingConnectorRegistry(IEnumerable<IStatutoryFilingConnector> connectors) : IStatutoryFilingConnectorRegistry
{
    private readonly IReadOnlyDictionary<StatutoryFilingConnectorType, IStatutoryFilingConnector> connectors = connectors.ToDictionary(x => x.Type);
    public IStatutoryFilingConnector Resolve(StatutoryFilingConnectorType type) => connectors.TryGetValue(type, out var connector) ? connector : throw new InvalidOperationException($"No statutory filing connector is registered for {type}.");
}
