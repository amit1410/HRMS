namespace HRMS.Domain.Common;

/// <summary>
/// Base type for primary domain entities that use a <see cref="Guid"/> key and carry audit timestamps.
/// Reference/lookup entities (e.g. Role, Permission) intentionally do not derive from this.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }

    /// <summary>UTC timestamp set once when the entity is first persisted.</summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>UTC timestamp set on every subsequent update. Null until the first update.</summary>
    public DateTime? ModifiedDate { get; set; }
}
