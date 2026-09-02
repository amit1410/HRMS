using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A state/province within a <see cref="Country"/>. Global (not tenant-scoped).
/// </summary>
public class State : BaseEntity
{
    public Guid CountryId { get; set; }

    /// <summary>Short code for the state, e.g. "DL", "MH". Unique within the country.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Navigation
    public Country? Country { get; set; }
    public ICollection<City> Cities { get; set; } = new List<City>();
}
