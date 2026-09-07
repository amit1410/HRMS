namespace HRMS.Application.Abstractions;

/// <summary>Request actor for catalog-backed platform operations; deliberately has no tenant state.</summary>
public interface IPlatformContext
{
    Guid? UserId { get; }
    int? SecurityRevision { get; }
    bool HasSecurityRevision { get; }
}
