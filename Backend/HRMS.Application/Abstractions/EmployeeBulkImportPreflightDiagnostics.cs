namespace HRMS.Application.Abstractions;

public enum EmployeeBulkImportPreflightStage
{
    Configuration,
    Catalog,
    Authentication,
    Authorization,
    Operation,
    Artifact,
    Validation
}

/// <summary>Fixed, non-sensitive input errors and failure categories for the local read-only verifier.</summary>
public static class EmployeeBulkImportPreflightDiagnostics
{
    public const string ApprovalPhrase = "READ-ONLY WEB1 b89d04aa-1419-4c85-a7e3-48ab1d92d111";

    public static string? InteractiveInputError(bool inputRedirected) =>
        inputRedirected ? "invocation.redirected_input_unsupported" : null;

    public static string? ApprovalError(string? supplied) =>
        string.Equals(supplied, ApprovalPhrase, StringComparison.Ordinal)
            ? null
            : string.IsNullOrWhiteSpace(supplied) ? "invocation.approval_missing" : "invocation.approval_invalid";

    public static string? JwtInputError(string? supplied) =>
        string.IsNullOrWhiteSpace(supplied) ? "authentication.missing_jwt" : null;

    public static string? JwtAuthenticationError(bool authenticated) =>
        authenticated ? null : "authentication.invalid_jwt";

    public const string MissingEmployeeImportPermission = "authorization.employee_import_missing";
    public const string TenantMismatch = "authorization.tenant_mismatch";

    public static string FailureCategory(EmployeeBulkImportPreflightStage stage) => stage switch
    {
        EmployeeBulkImportPreflightStage.Configuration => "configuration.invalid_or_missing",
        EmployeeBulkImportPreflightStage.Catalog => "catalog.routing_failed",
        EmployeeBulkImportPreflightStage.Authentication => "authentication.invalid_jwt",
        EmployeeBulkImportPreflightStage.Authorization => "authorization.failed",
        EmployeeBulkImportPreflightStage.Operation => "operation.verification_failed",
        EmployeeBulkImportPreflightStage.Artifact => "artifact.verification_failed",
        EmployeeBulkImportPreflightStage.Validation => "validation.failed",
        _ => "verification.failed"
    };
}
