namespace HRMS.Application.Abstractions;

public sealed class EmployeeBulkUploadOptions
{
    public const string SectionName = "EmployeeBulkUpload";
    public const int MaximumAllowedEmployeeRows = 10_000;

    public int MaximumEmployeeRows { get; init; } = MaximumAllowedEmployeeRows;

    public string? Validate()
        => MaximumEmployeeRows is < 1 or > MaximumAllowedEmployeeRows
            ? $"{SectionName}:MaximumEmployeeRows must be between 1 and {MaximumAllowedEmployeeRows:N0}."
            : null;
}
