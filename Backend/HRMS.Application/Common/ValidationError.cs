namespace HRMS.Application.Common;

/// <summary>A single field-level validation failure returned in the standard error envelope.</summary>
public class ValidationError
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public ValidationError() { }

    public ValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }
}
