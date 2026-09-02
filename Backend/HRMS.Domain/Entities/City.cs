using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A city within a <see cref="State"/>. Global (not tenant-scoped).
/// </summary>
public class City : BaseEntity
{
    public Guid StateId { get; set; }

    /// <summary>Short code for the city, e.g. "NDL", "MUM". Unique within the state.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Navigation
    public State? State { get; set; }
}
