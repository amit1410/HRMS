namespace HRMS.Application.Common;

/// <summary>
/// Outcome category of an application-service call. The API layer maps these to HTTP status codes, so
/// services never reference HTTP concepts and expected failures never travel as exceptions.
/// </summary>
public enum ResultStatus
{
    Success = 0,
    ValidationFailed = 1,
    Unauthorized = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    ServiceUnavailable = 6
}

/// <summary>
/// Result of an application-service call that returns a value. Expected failures (bad credentials, a
/// deactivated account) are ordinary return values rather than exceptions, which keeps the happy path
/// readable and lets the controller translate outcomes to status codes in one place.
/// </summary>
public sealed class Result<T>
{
    private Result(bool succeeded, ResultStatus status, string message, T? value, IReadOnlyList<ValidationError>? errors,
        string? diagnosticStage = null, string? diagnosticCode = null, int? diagnosticRowNumber = null,
        string? diagnosticOperation = null, string? diagnosticResourceType = null, string? diagnosticExceptionType = null,
        string? diagnosticHResult = null, bool? diagnosticResourceInsideWorkspace = null)
    {
        Succeeded = succeeded;
        Status = status;
        Message = message;
        Value = value;
        Errors = errors;
        DiagnosticStage = diagnosticStage;
        DiagnosticCode = diagnosticCode;
        DiagnosticRowNumber = diagnosticRowNumber;
        DiagnosticOperation = diagnosticOperation;
        DiagnosticResourceType = diagnosticResourceType;
        DiagnosticExceptionType = diagnosticExceptionType;
        DiagnosticHResult = diagnosticHResult;
        DiagnosticResourceInsideWorkspace = diagnosticResourceInsideWorkspace;
    }

    public bool Succeeded { get; }
    public ResultStatus Status { get; }
    public string Message { get; }
    public T? Value { get; }
    public IReadOnlyList<ValidationError>? Errors { get; }
    /// <summary>Safe internal diagnostic metadata. API error envelopes deliberately do not expose it.</summary>
    public string? DiagnosticStage { get; }
    public string? DiagnosticCode { get; }
    public int? DiagnosticRowNumber { get; }
    public string? DiagnosticOperation { get; }
    public string? DiagnosticResourceType { get; }
    public string? DiagnosticExceptionType { get; }
    public string? DiagnosticHResult { get; }
    public bool? DiagnosticResourceInsideWorkspace { get; }

    public static Result<T> Success(T value, string message = "Success") =>
        new(true, ResultStatus.Success, message, value, null);

    public static Result<T> Failure(ResultStatus status, string message, IReadOnlyList<ValidationError>? errors = null,
        string? diagnosticStage = null, string? diagnosticCode = null, int? diagnosticRowNumber = null,
        string? diagnosticOperation = null, string? diagnosticResourceType = null, string? diagnosticExceptionType = null,
        string? diagnosticHResult = null, bool? diagnosticResourceInsideWorkspace = null) =>
        new(false, status, message, default, errors, diagnosticStage, diagnosticCode, diagnosticRowNumber,
            diagnosticOperation, diagnosticResourceType, diagnosticExceptionType, diagnosticHResult, diagnosticResourceInsideWorkspace);

    public static Result<T> Unauthorized(string message) => Failure(ResultStatus.Unauthorized, message);

    public static Result<T> Forbidden(string message) => Failure(ResultStatus.Forbidden, message);

    public static Result<T> NotFound(string message) => Failure(ResultStatus.NotFound, message);

    public static Result<T> Unavailable(string message) => Failure(ResultStatus.ServiceUnavailable, message);

    /// <summary>A uniqueness or dependency conflict — the request is well formed but the state forbids it.</summary>
    public static Result<T> Conflict(string message, IReadOnlyList<ValidationError>? errors = null) =>
        Failure(ResultStatus.Conflict, message, errors);

    /// <summary>
    /// A rule that could not be checked by shape validation alone, e.g. a referenced record that does not
    /// exist within the caller's tenant. Reported with field-level errors so a client can mark the input.
    /// </summary>
    public static Result<T> Invalid(string message, IReadOnlyList<ValidationError>? errors = null) =>
        Failure(ResultStatus.ValidationFailed, message, errors);

    /// <summary>Convenience for the common single-field case.</summary>
    public static Result<T> Invalid(string field, string message) =>
        Failure(ResultStatus.ValidationFailed, message, [new ValidationError(field, message)]);
}
