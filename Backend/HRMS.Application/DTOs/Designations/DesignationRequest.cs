namespace HRMS.Application.DTOs.Designations;

/// <summary>
/// Create/update payload for a designation. As with departments, one write model serves both verbs and the
/// tenant is never accepted from the client.
/// </summary>
public class DesignationRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
