namespace HRMS.Application.Common;

/// <summary>Consistent display-only masking for sensitive employee and banking values.</summary>
public static class SensitiveDataMasker
{
    public static string? Aadhaar(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null :
        value.Length >= 4 ? $"XXXX-XXXX-{value[^4..]}" : "XXXX";

    public static string? Pan(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null :
        value.Length >= 2 ? $"{value[..1]}****{value[^1..]}" : "****";

    public static string? Identifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null :
        value.Length >= 4 ? $"******{value[^4..]}" : "****";

    public static string AccountNumber(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty :
        value.Length > 4 ? $"********{value[^4..]}" : new string('*', value.Length);

    public static string? Ifsc(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null :
        value.Length > 6 ? $"{value[..4]}*****{value[^2..]}" : new string('*', value.Length);
}
