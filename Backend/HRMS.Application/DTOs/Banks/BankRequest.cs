using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Banks;

/// <summary>
/// Create/update payload for a bank. One write model serves both: the fields are identical, and PUT
/// replaces the whole record, so an omitted optional field is cleared rather than left as it was.
/// <para>
/// There is deliberately no TenantId here. The tenant comes from the caller's authenticated token; a
/// client-supplied one would be ignored at best and a cross-tenant write at worst.
/// </para>
/// </summary>
public class BankRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ShortName { get; set; }

    public string? IfscPrefix { get; set; }

    public BankType? BankType { get; set; }

    public string? Country { get; set; }

    public DateOnly? EffectiveFrom { get; set; }

    public string? Remarks { get; set; }

    public bool IsActive { get; set; } = true;
}
