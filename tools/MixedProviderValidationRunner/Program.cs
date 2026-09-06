using System.Data.Common;
using System.Globalization;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using HRMS.Domain.Enums;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

const string sqlProvider = "Microsoft.EntityFrameworkCore.SqlServer";
const string mySqlProvider = "MySql.EntityFrameworkCore";

var arguments = ParseArguments(args);
var sqlDatabase = Required(arguments, "sql-database");
var mySqlShardKey = Required(arguments, "mysql-shard-key");
var expectedMySqlDatabase = Required(arguments, "expected-mysql-database");
var mysqlLowerCaseTableNamesText = Required(arguments, "mysql-lower-case-table-names");
var setupOnly = arguments.ContainsKey("setup-only");

if (string.IsNullOrWhiteSpace(sqlDatabase)
    || string.IsNullOrWhiteSpace(mySqlShardKey)
    || string.IsNullOrWhiteSpace(expectedMySqlDatabase)
    || string.IsNullOrWhiteSpace(mysqlLowerCaseTableNamesText))
{
    throw new InvalidOperationException("All database identity arguments are required.");
}

if (!int.TryParse(mysqlLowerCaseTableNamesText, out var mysqlLowerCaseTableNames)
    || mysqlLowerCaseTableNames is < 0 or > 2)
{
    throw new ArgumentException("--mysql-lower-case-table-names must be 0, 1, or 2.");
}

var configuration = new ConfigurationManager();
CopyRequiredEnvironmentSetting(
    configuration,
    "Database:CatalogProvider",
    "Database__CatalogProvider");
CopyRequiredEnvironmentSetting(
    configuration,
    "ConnectionStrings:Catalog",
    "ConnectionStrings__Catalog");
CopyRequiredEnvironmentSetting(
    configuration,
    "ConnectionStrings:SqlServer",
    "ConnectionStrings__SqlServer");
CopyOptionalEnvironmentSetting(
    configuration,
    "Sharding:SqlServerConnectionStringTemplate",
    "Sharding__SqlServerConnectionStringTemplate");
CopyOptionalEnvironmentSetting(
    configuration,
    "Sharding:ConnectionStringTemplate",
    "Sharding__ConnectionStringTemplate");
CopyRequiredEnvironmentSetting(
    configuration,
    "Sharding:MySqlConnectionStringTemplate",
    "Sharding__MySqlConnectionStringTemplate");

var sqlTemplate = configuration["Sharding:SqlServerConnectionStringTemplate"];
var legacySqlTemplate = configuration["Sharding:ConnectionStringTemplate"];
var sharedSqlConnection = configuration.GetConnectionString("SqlServer");
var sqlUsesTemplate = !string.IsNullOrWhiteSpace(sqlTemplate)
    || !string.IsNullOrWhiteSpace(legacySqlTemplate);
var sqlUsesSharedDatabase = !sqlUsesTemplate && !string.IsNullOrWhiteSpace(sharedSqlConnection);
if (!sqlUsesTemplate && !sqlUsesSharedDatabase)
    throw new InvalidOperationException("SQL Server tenant routing configuration is incomplete.");

var mySqlTemplate = configuration["Sharding:MySqlConnectionStringTemplate"]!;
if (!mySqlTemplate.Contains(ShardingOptions.ShardKeyPlaceholder, StringComparison.Ordinal))
    throw new InvalidOperationException("Sharding:MySqlConnectionStringTemplate must contain the production shard placeholder.");

Console.WriteLine("MIXED PROVIDER RUNNER SETUP");
Console.WriteLine("Catalog provider: SqlServer");
Console.WriteLine($"SQL Server routing mode: {(sqlUsesTemplate ? "TEMPLATE" : "SHARED")}");
Console.WriteLine("MySQL routing mode: TEMPLATE");
Console.WriteLine($"Expected SQL database: {sqlDatabase}");
Console.WriteLine($"Expected MySQL database: {expectedMySqlDatabase}");

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder => builder.ClearProviders());
services.AddSingleton<ITenantContext, ReadOnlyTenantContext>();
services.AddSingleton<TimeProvider>(TimeProvider.System);
services.AddSingleton<IOptions<JwtSettings>>(Options.Create(new JwtSettings
{
    Issuer = "ExternalMixedProviderValidation",
    Audience = "ExternalMixedProviderValidation",
    SecretKey = "external-runner-diagnostic-placeholder-key-32",
    AccessTokenMinutes = 60,
    RefreshTokenDays = 7,
    ClockSkewSeconds = 30
}));
services.AddInfrastructure(configuration);

await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true
});

Console.WriteLine("Service provider validation: PASS");

if (setupOnly)
{
    Console.WriteLine("SETUP-ONLY: no catalog or tenant database access performed.");
    return;
}

var sqlDescriptor = await ResolveDemo01Async(serviceProvider);
if (sqlDescriptor is null)
    throw new InvalidOperationException("DEMO01 was not resolved from the catalog.");

if (sqlDescriptor.DatabaseProvider != DatabaseProviderType.SqlServer)
    throw new InvalidOperationException("DEMO01 is not configured for SqlServer.");

await ValidateTenantAsync(
    serviceProvider,
    sqlDescriptor,
    sqlProvider,
    sqlDatabase,
    "SQL SERVER VALIDATION");

var mySqlDescriptor = new ShardDescriptor(
    Guid.Empty,
    "EXTERNAL_MYSQL_VALIDATION",
    "external-validation.localhost",
    mySqlShardKey,
    TenantStatus.Active,
    DatabaseProviderType.MySql);

await ValidateTenantAsync(
    serviceProvider,
    mySqlDescriptor,
    mySqlProvider,
    expectedMySqlDatabase,
    "MYSQL VALIDATION",
    mysqlLowerCaseTableNames);

Console.WriteLine("EXTERNAL MIXED-PROVIDER ROUTING VALIDATION PASS");
Console.WriteLine("Scope: provider-selection and tenant DbContext path outside HTTP.");
Console.WriteLine("HTTP Host -> catalog MySQL-row routing remains unverified by design.");

static async Task<ShardDescriptor?> ResolveDemo01Async(ServiceProvider serviceProvider)
{
    await using var scope = serviceProvider.CreateAsyncScope();
    var resolver = scope.ServiceProvider.GetRequiredService<ITenantShardResolver>();
    return await resolver.ResolveByHostAsync("demo01.localhost");
}

static async Task ValidateTenantAsync(
    ServiceProvider serviceProvider,
    ShardDescriptor descriptor,
    string expectedProvider,
    string expectedDatabase,
    string heading,
    int? mysqlLowerCaseTableNames = null)
{
    await using var scope = serviceProvider.CreateAsyncScope();
    var shardContext = scope.ServiceProvider.GetRequiredService<IShardContext>();
    shardContext.Use(descriptor);

    var db = scope.ServiceProvider.GetRequiredService<HrmsDbContext>();
    var actualProvider = db.Database.ProviderName;
    if (!string.Equals(actualProvider, expectedProvider, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"{heading}: expected provider '{expectedProvider}', received '{actualProvider ?? "unknown"}'.");
    }

    var connection = db.Database.GetDbConnection();
    var databaseBeforeOpen = connection.Database;
    Console.WriteLine(heading);
    Console.WriteLine($"TenantCode: {descriptor.TenantCode}");
    Console.WriteLine($"ShardKey: {descriptor.ShardKey}");
    Console.WriteLine($"Configured provider: {descriptor.DatabaseProvider}");
    Console.WriteLine($"EF ProviderName: {actualProvider}");
    Console.WriteLine($"Connection type: {connection.GetType().FullName}");
    Console.WriteLine($"Configured database before open: {databaseBeforeOpen}");

    var preOpenComparison = descriptor.DatabaseProvider == DatabaseProviderType.MySql
        && mysqlLowerCaseTableNames is 1 or 2
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    if (descriptor.DatabaseProvider == DatabaseProviderType.MySql)
        Console.WriteLine($"MySQL lower_case_table_names: {mysqlLowerCaseTableNames}");

    if (!string.Equals(databaseBeforeOpen, expectedDatabase, preOpenComparison))
    {
        throw new InvalidOperationException(
            $"{heading}: resolved database '{databaseBeforeOpen}' does not match the approved expected database.");
    }

    await connection.OpenAsync();
    try
    {
        if (descriptor.DatabaseProvider == DatabaseProviderType.SqlServer)
        {
            if (!string.Equals(connection.Database, expectedDatabase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{heading}: opened database '{connection.Database}' does not match the approved expected database.");
            }

            Console.WriteLine($"Opened database: {connection.Database}");
        }
        else
        {
            if (mysqlLowerCaseTableNames is null)
                throw new InvalidOperationException("MYSQL VALIDATION: approved lower_case_table_names is missing.");

            var serverIdentity = await ReadMySqlServerIdentityAsync(connection);
            if (serverIdentity.LowerCaseTableNames != mysqlLowerCaseTableNames.Value)
            {
                throw new InvalidOperationException(
                    "MYSQL VALIDATION: server lower_case_table_names does not match the approved preflight value.");
            }

            if (!string.Equals(serverIdentity.Database, expectedDatabase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"MYSQL VALIDATION: server DATABASE() '{serverIdentity.Database}' does not match the approved database identity.");
            }

            Console.WriteLine($"Connection.Database after open: {connection.Database}");
            Console.WriteLine($"Server lower_case_table_names: {serverIdentity.LowerCaseTableNames}");
            Console.WriteLine($"Server DATABASE(): {serverIdentity.Database}");
        }

        Console.WriteLine("Database identity: PASS");
        var count = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync();

        Console.WriteLine($"Read-only Users.CountAsync: PASS ({count})");
    }
    finally
    {
        await connection.CloseAsync();
    }
}

static async Task<(int LowerCaseTableNames, string Database)> ReadMySqlServerIdentityAsync(
    DbConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT @@lower_case_table_names, DATABASE();";

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        throw new InvalidOperationException("MYSQL VALIDATION: server identity query returned no rows.");

    var modeValue = reader.GetValue(0);
    var databaseValue = reader.GetValue(1);
    if (modeValue is DBNull || databaseValue is DBNull)
        throw new InvalidOperationException("MYSQL VALIDATION: server identity query returned NULL.");

    if (!int.TryParse(
            Convert.ToString(modeValue, CultureInfo.InvariantCulture),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var lowerCaseTableNames))
    {
        throw new InvalidOperationException("MYSQL VALIDATION: server lower_case_table_names was not an integer.");
    }

    var database = Convert.ToString(databaseValue, CultureInfo.InvariantCulture);
    if (string.IsNullOrWhiteSpace(database))
        throw new InvalidOperationException("MYSQL VALIDATION: server DATABASE() was blank.");

    if (await reader.ReadAsync())
        throw new InvalidOperationException("MYSQL VALIDATION: server identity query returned multiple rows.");

    return (lowerCaseTableNames, database);
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index++)
    {
        if (!args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Arguments must use --name value form.");

        var name = args[index][2..];
        if (string.Equals(name, "setup-only", StringComparison.OrdinalIgnoreCase))
        {
            values[name] = "true";
            continue;
        }

        if (index + 1 >= args.Length)
            throw new ArgumentException("Arguments must use --name value form.");

        values[name] = args[++index];
    }

    return values;
}

static string Required(IReadOnlyDictionary<string, string> arguments, string name) =>
    arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required argument --{name}.");

static void CopyRequiredEnvironmentSetting(
    ConfigurationManager configuration,
    string configurationKey,
    string environmentVariable)
{
    var value = Environment.GetEnvironmentVariable(environmentVariable);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"Required configuration setting '{configurationKey}' is missing.");

    configuration[configurationKey] = value;
}

static void CopyOptionalEnvironmentSetting(
    ConfigurationManager configuration,
    string configurationKey,
    string environmentVariable)
{
    var value = Environment.GetEnvironmentVariable(environmentVariable);
    if (!string.IsNullOrWhiteSpace(value))
        configuration[configurationKey] = value;
}

sealed class ReadOnlyTenantContext : ITenantContext
{
    public Guid? TenantId => null;

    public Guid? UserId => null;

    public bool HasTenant => false;
}
