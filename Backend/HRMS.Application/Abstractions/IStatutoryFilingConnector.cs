using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed record StatutoryFilingConnectorResult(StatutoryFilingSubmissionOutcome Outcome, string? ExternalReference, string? ResponseCode, string SafeResponseSummary, bool Retryable);

public interface IStatutoryFilingConnector
{
    StatutoryFilingConnectorType Type { get; }
    Task<Result<string?>> ValidateConfigurationAsync(StatutoryFilingConnectionProfile? profile, CancellationToken ct = default);
    Task<Result<StatutoryFilingConnectorResult>> SubmitAsync(StatutoryFilingPackage package, StatutoryFilingConnectionProfile? profile, CancellationToken ct = default);
    Task<Result<StatutoryFilingConnectorResult>> RefreshStatusAsync(StatutoryFilingSubmission submission, StatutoryFilingConnectionProfile? profile, CancellationToken ct = default);
}

public interface IStatutoryFilingConnectorRegistry
{
    IStatutoryFilingConnector Resolve(StatutoryFilingConnectorType type);
}
