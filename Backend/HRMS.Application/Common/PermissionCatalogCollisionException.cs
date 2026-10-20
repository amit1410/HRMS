namespace HRMS.Application.Common;

/// <summary>A canonical permission catalog mapping conflicts with existing reference data.</summary>
public sealed class PermissionCatalogCollisionException(string message) : Exception(message);
