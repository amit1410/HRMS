using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlReviewRoundMigrationTests
{
    private const string BeforeReviewRound = "20261002011904_AddConfirmationDecisionEngine";
    private const string ReviewRound = "20261004064500_AddReviewRoundToEntities";

    [Fact]
    public async Task Current_index_name_migrates_to_the_expected_schema()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateToAsync(database.ConnectionString, BeforeReviewRound);
        await MigrateToAsync(database.ConnectionString, ReviewRound);
        await AssertFinalSchemaAsync(database.ConnectionString);
    }

    [Fact]
    public async Task Known_legacy_index_name_migrates_to_the_expected_schema()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateToAsync(database.ConnectionString, BeforeReviewRound);
        await ExecuteAsync(database.ConnectionString, "ALTER TABLE `ConfirmationReviewerReviews` RENAME INDEX `IX_ConfReviewerReviews_Tenant_Case_Type_Version` TO `IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseI~`;");
        await MigrateToAsync(database.ConnectionString, ReviewRound);
        await AssertFinalSchemaAsync(database.ConnectionString);
    }

    [Fact]
    public async Task Already_replaced_partial_state_is_completed_without_duplicate_schema_objects()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateToAsync(database.ConnectionString, BeforeReviewRound);
        await ExecuteAsync(database.ConnectionString, "ALTER TABLE `ConfirmationReviewCases` ADD COLUMN `ReviewRound` int NOT NULL DEFAULT 1; ALTER TABLE `ConfirmationReviewerReviews` ADD COLUMN `ReviewRound` int NOT NULL DEFAULT 1; ALTER TABLE `ConfirmationDecisions` ADD COLUMN `ReviewRound` int NOT NULL DEFAULT 1; ALTER TABLE `ConfirmationDecisionCalculations` ADD COLUMN `ReviewRound` int NOT NULL DEFAULT 1; CREATE UNIQUE INDEX `IX_ConfReviewerReviews_Tenant_Case_Round_Type_Version` ON `ConfirmationReviewerReviews` (`TenantId`, `ConfirmationReviewCaseId`, `ReviewRound`, `ReviewerType`, `VersionNumber`);");
        await MigrateToAsync(database.ConnectionString, ReviewRound);
        await AssertFinalSchemaAsync(database.ConnectionString);
    }

    [Fact]
    public async Task Unknown_index_name_fails_clearly()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateToAsync(database.ConnectionString, BeforeReviewRound);
        await ExecuteAsync(database.ConnectionString, "ALTER TABLE `ConfirmationReviewerReviews` RENAME INDEX `IX_ConfReviewerReviews_Tenant_Case_Type_Version` TO `IX_Unexpected_Review_Index`;");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => MigrateToAsync(database.ConnectionString, ReviewRound));
        Assert.Contains("Known pre-ReviewRound reviewer index was not found", Flatten(error), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<DisposableDatabase> CreateDatabaseAsync()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("MySQL migration variant tests require HRMS_MYSQL_TEST_CONNECTION.");

        var normalized = TestSupport.MySqlApiFactory.NormalizeConnectionString(configured);
        var name = $"HRMS_ReviewRound_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var target = new MySqlConnectionStringBuilder(normalized) { Database = name };
        await using var connection = new MySqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{name}`;";
        await command.ExecuteNonQueryAsync();
        return new DisposableDatabase(target.ConnectionString, admin.ConnectionString, name);
    }

    private static async Task MigrateToAsync(string connectionString, string migration)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.MigrateAsync(migration);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertFinalSchemaAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal(4, await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND column_name = 'ReviewRound' AND table_name IN ('ConfirmationReviewCases','ConfirmationReviewerReviews','ConfirmationDecisions','ConfirmationDecisionCalculations');"));
        Assert.Equal(5, await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'ConfirmationReviewerReviews' AND index_name = 'IX_ConfReviewerReviews_Tenant_Case_Round_Type_Version';"));
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'ConfirmationReviewerReviews' AND index_name IN ('IX_ConfReviewerReviews_Tenant_Case_Type_Version','IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseI~','IX_Unexpected_Review_Index');"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema = DATABASE() AND table_name = 'ConfirmationReviewerReviews' AND constraint_type = 'FOREIGN KEY';"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '__EFMigrationsHistory';"));
    }

    private static async Task<long> ScalarAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static HrmsDbContext CreateContext(string connectionString) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext());

    private static string Flatten(Exception error) => string.Join(" ", Enumerate(error).Select(x => x.Message));
    private static IEnumerable<Exception> Enumerate(Exception error) { for (var current = error; current is not null; current = current.InnerException) yield return current; }

    private sealed class DisposableDatabase(string connectionString, string adminConnectionString, string name) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;
        public async ValueTask DisposeAsync()
        {
            await using var connection = new MySqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS `{name}`;";
            await command.ExecuteNonQueryAsync();
        }
    }
}
