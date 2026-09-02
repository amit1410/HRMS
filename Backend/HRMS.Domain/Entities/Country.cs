using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A country reference record. Global (not tenant-scoped): every tenant shares the same country list.
/// </summary>
public class Country : BaseEntity
{
    /// <summary>ISO 3166-1 alpha-2 code, e.g. "IN", "US". Unique globally.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<State> States { get; set; } = new List<State>();
}
