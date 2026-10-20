namespace HRMS.Application.Common;

/// <summary>
/// Shared shape rules for the statutory fields that are mandatory when an employee is created.
/// The rules are intentionally not applied as required rules to update DTO validators because updates
/// support masked/blank sensitive values and must retain their existing behavior.
/// </summary>
public static class EmployeeCreationValidation
{
    public const string AadhaarPattern = @"^\d{12}$";
    public const string PanPattern = @"^[A-Z]{5}\d{4}[A-Z]$";

    public static IReadOnlyList<ValidationError> ValidateRequiredFields(
        string? jobStatus,
        string? aadhaarNumber,
        string? panNumber)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(jobStatus))
            errors.Add(new("jobStatus", "Job status is required."));
        else if (jobStatus.Trim().Length > 100)
            errors.Add(new("jobStatus", "Job status must not exceed 100 characters."));

        if (string.IsNullOrWhiteSpace(aadhaarNumber))
            errors.Add(new("aadhaarNumber", "Aadhaar number is required."));
        else if (!System.Text.RegularExpressions.Regex.IsMatch(aadhaarNumber.Trim(), AadhaarPattern))
            errors.Add(new("aadhaarNumber", "Aadhaar number must be exactly 12 digits."));

        if (string.IsNullOrWhiteSpace(panNumber))
            errors.Add(new("panNumber", "PAN number is required."));
        else if (!System.Text.RegularExpressions.Regex.IsMatch(
                     panNumber.Trim(), PanPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            errors.Add(new("panNumber", "PAN must be in the format ABCDE1234F (5 letters, 4 digits, 1 letter)."));

        return errors;
    }
}
