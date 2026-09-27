using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class SqliteInMemoryDatabaseTests
{
    [Fact]
    public void Dispose_is_idempotent()
    {
        var database = new SqliteInMemoryDatabase();

        database.Dispose();
        database.Dispose();
    }
}
