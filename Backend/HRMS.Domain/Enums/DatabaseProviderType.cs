namespace HRMS.Domain.Enums;

/// <summary>
/// The provider used by a tenant database. SQLite remains an explicit local
/// development/test configuration and is intentionally not a tenant routing value.
/// </summary>
public enum DatabaseProviderType
{
    SqlServer = 1,
    MySql = 2
}
