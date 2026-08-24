namespace HRMS.Application.Common;

/// <summary>
/// Standard API response envelope with no payload. See <see cref="ApiResponse{T}"/> for responses
/// that carry data. Shape: { success, message, errors? }.
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Populated only for validation/business failures. Omitted from JSON when null.</summary>
    public IReadOnlyList<ValidationError>? Errors { get; set; }

    public static ApiResponse Ok(string message = "Success") =>
        new() { Success = true, Message = message };

    public static ApiResponse Fail(string message, IReadOnlyList<ValidationError>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };
}

/// <summary>
/// Standard API response envelope carrying a typed payload.
/// Shape: { success, message, data, errors? }.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public IReadOnlyList<ValidationError>? Errors { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "Success") =>
        new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message, IReadOnlyList<ValidationError>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };
}
