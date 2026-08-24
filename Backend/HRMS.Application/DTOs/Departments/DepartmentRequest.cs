namespace HRMS.Application.DTOs.Departments;

/// <summary>
/// Create/update payload for a department. One write model serves both: the fields are identical, and PUT
/// replaces the whole record, so an omitted optional field is cleared rather than left as it was.
/// <para>
/// There is deliberately no TenantId here. The tenant comes from the caller's authenticated token; a
/// client-supplied one would be ignored at best and a cross-tenant write at worst.
/// </para>
/// </summary>
public class DepartmentRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
