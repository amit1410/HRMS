namespace HRMS.Application.Abstractions;

/// <summary>Configuration for a disabled-by-default, tenant- and operation-scoped import worker.</summary>
public sealed class EmployeeBulkImportWorkerOptions
{
    public const string SectionName = "EmployeeBulkImportWorker";
    public bool Enabled { get; init; }
    public string? TargetTenantCode { get; init; }
    public Guid? TargetOperationId { get; init; }
    public int PollIntervalSeconds { get; init; } = 10;

    public string? Validate()
    {
        if (PollIntervalSeconds is < 1 or > 300)
            return $"{SectionName}:PollIntervalSeconds must be between 1 and 300.";

        var hasTenant = !string.IsNullOrWhiteSpace(TargetTenantCode);
        var hasOperation = TargetOperationId.HasValue;
        if (hasTenant != hasOperation)
            return $"{SectionName}:TargetTenantCode and TargetOperationId must be configured together.";
        if (Enabled && !hasTenant)
            return $"{SectionName} requires an exact target tenant and operation before it can be enabled.";
        if (TargetOperationId == Guid.Empty)
            return $"{SectionName}:TargetOperationId must be a non-empty GUID.";
        if (hasTenant && !IsValidTenantCode(TargetTenantCode!))
            return $"{SectionName}:TargetTenantCode contains unsupported characters.";

        return null;
    }

    internal static bool IsValidTenantCode(string value)
    {
        if (value.Length is < 1 or > 20 || value != value.ToUpperInvariant() || !char.IsAsciiLetterOrDigit(value[0])) return false;
        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}
