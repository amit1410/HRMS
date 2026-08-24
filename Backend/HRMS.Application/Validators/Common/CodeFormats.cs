namespace HRMS.Application.Validators.Common;

/// <summary>
/// The character set allowed in human-assigned codes (department, designation, employee). Constraining it
/// keeps codes usable as identifiers in URLs, spreadsheet exports and imports, and rules out the leading
/// characters a spreadsheet would treat as a formula.
/// </summary>
public static class CodeFormats
{
    public const string Pattern = @"^[A-Za-z0-9][A-Za-z0-9._\-/]*$";

    public const string Message =
        "Code must start with a letter or digit and may contain only letters, digits, dots, dashes, "
        + "underscores and slashes.";
}
