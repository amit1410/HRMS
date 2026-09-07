using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Text.Json;

if (!args.Contains("--confirm-platform-bootstrap", StringComparer.Ordinal))
    throw new InvalidOperationException("Refusing to run without explicit --confirm-platform-bootstrap intent.");

var configuration = new ConfigurationManager();
var repositoryRoot = FindRepositoryRoot();
var apiConfigurationRoot = Path.Combine(repositoryRoot, "Backend", "HRMS.API");
var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? "Production";
if (string.IsNullOrWhiteSpace(environmentName) || Path.GetFileName(environmentName) != environmentName)
    throw new InvalidOperationException("The configured environment name is invalid.");
LoadJsonConfiguration(configuration, Path.Combine(apiConfigurationRoot, "appsettings.json"), required: true);
LoadJsonConfiguration(configuration, Path.Combine(apiConfigurationRoot, $"appsettings.{environmentName}.json"), required: false);
CopyOptionalEnvironmentSetting(configuration, "Database:Provider", "Database__Provider");
CopyOptionalEnvironmentSetting(configuration, "Database:CatalogProvider", "Database__CatalogProvider");
CopyOptionalEnvironmentSetting(configuration, "ConnectionStrings:Catalog", "ConnectionStrings__Catalog");

var catalogProvider = configuration["Database:CatalogProvider"];
if (string.IsNullOrWhiteSpace(catalogProvider))
    catalogProvider = string.Equals(configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase) ? "Sqlite" : "SqlServer";
configuration["Database:CatalogProvider"] = catalogProvider;
if (!string.Equals(catalogProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Platform bootstrap requires Database:CatalogProvider=SqlServer.");
if (string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Catalog"]))
    throw new InvalidOperationException("Missing required setting: ConnectionStrings:Catalog.");
configuration["Sharding:CacheSeconds"] = "30";
configuration["Sharding:UnknownHostCacheSeconds"] = "5";

Console.WriteLine($"Environment: {environmentName}");
Console.WriteLine("Catalog provider: SqlServer");

var email = ReadRequired("Platform administrator email");
var firstName = ReadRequired("First name");
var lastName = ReadRequired("Last name");
var password = ReadSecret("Password");
var confirm = ReadSecret("Confirm password");
try
{
    if (!string.Equals(password, confirm, StringComparison.Ordinal)) throw new InvalidOperationException("Passwords do not match.");
    ValidateEmail(email);

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddSingleton<TimeProvider>(TimeProvider.System);
    services.AddSingleton<IPlatformContext, BootstrapPlatformContext>();
    services.AddInfrastructure(configuration);
    using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    using var scope = provider.CreateScope();
    var catalog = scope.ServiceProvider.GetRequiredService<HrmsCatalogDbContext>();

    await VerifyCatalogIdentityAsync(catalog);
    await VerifyCatalogPrerequisitesAsync(catalog);

    var normalized = email.ToUpperInvariant();
    if (await catalog.PlatformUsers.AnyAsync(x => x.NormalizedEmail == normalized))
        throw new InvalidOperationException("A platform user with that email already exists.");

    var role = await catalog.PlatformRoles.SingleAsync(x => x.Name == PlatformRoleNames.PlatformSuperAdmin);
    var permissions = await catalog.PlatformPermissions
        .Where(x => PlatformPermissions.All.Contains(x.Name))
        .ToListAsync();
    if (permissions.Count != PlatformPermissions.All.Count)
        throw new InvalidOperationException("Platform permission prerequisites are incomplete.");

    var grants = await catalog.PlatformRolePermissions
        .Where(x => x.PlatformRoleId == role.Id)
        .ToListAsync();
    if (grants.Count != PlatformPermissions.All.Count
        || grants.Any(x => permissions.All(p => p.Id != x.PlatformPermissionId)))
        throw new InvalidOperationException("PlatformSuperAdmin grants are not exactly the expected three permissions.");

    var passwordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password);
    password = string.Empty;
    confirm = string.Empty;
    var now = DateTime.UtcNow;
    var user = new PlatformUser
    {
        Id = Guid.NewGuid(), Email = email, NormalizedEmail = normalized, FirstName = firstName, LastName = lastName,
        PasswordHash = passwordHash,
        IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, SecurityRevision = 0
    };
    await using var transaction = await catalog.Database.BeginTransactionAsync();
    catalog.PlatformUsers.Add(user);
    catalog.PlatformUserRoles.Add(new PlatformUserRole { PlatformUserId = user.Id, PlatformRoleId = role.Id });
    await catalog.SaveChangesAsync();
    await transaction.CommitAsync();

    await VerifyCreatedUserAsync(catalog, user.Id, normalized, role.Id);
    Console.WriteLine($"PlatformSuperAdmin created: {user.Id} ({user.Email})");
}
finally
{
    password = string.Empty;
    confirm = string.Empty;
}

static string FindRepositoryRoot()
{
    var starts = new[]
    {
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory
    };
    foreach (var start in starts)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            var apiSettings = Path.Combine(directory.FullName, "Backend", "HRMS.API", "appsettings.json");
            if (File.Exists(Path.Combine(directory.FullName, "HRMS.slnx")) && File.Exists(apiSettings))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("Could not locate the HRMS repository and Backend/HRMS.API/appsettings.json.");
}

static void LoadJsonConfiguration(ConfigurationManager configuration, string path, bool required)
{
    if (!File.Exists(path))
    {
        if (required) throw new InvalidOperationException($"Required configuration file was not found: {path}.");
        return;
    }
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    AddJsonValues(configuration, document.RootElement, string.Empty);
}

static void AddJsonValues(ConfigurationManager configuration, JsonElement element, string prefix)
{
    switch (element.ValueKind)
    {
        case JsonValueKind.Object:
            foreach (var property in element.EnumerateObject())
                AddJsonValues(configuration, property.Value, JoinConfigurationKey(prefix, property.Name));
            break;
        case JsonValueKind.Array:
            var index = 0;
            foreach (var item in element.EnumerateArray())
                AddJsonValues(configuration, item, JoinConfigurationKey(prefix, index++.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            break;
        case JsonValueKind.Null:
            configuration[prefix] = null;
            break;
        default:
            configuration[prefix] = element.ToString();
            break;
    }
}

static string JoinConfigurationKey(string prefix, string child) =>
    string.IsNullOrEmpty(prefix) ? child : $"{prefix}:{child}";

static void CopyOptionalEnvironmentSetting(ConfigurationManager configuration, string key, string environmentName)
{
    var value = Environment.GetEnvironmentVariable(environmentName);
    if (!string.IsNullOrWhiteSpace(value)) configuration[key] = value;
}

static string ReadRequired(string label)
{
    Console.Write($"{label}: ");
    var value = Console.ReadLine()?.Trim();
    return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{label} is required.") : value;
}

static string ReadSecret(string label)
{
    Console.Write($"{label}: ");
    var chars = new List<char>();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && chars.Count > 0) { chars.RemoveAt(chars.Count - 1); continue; }
        if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
    }
    Console.WriteLine();
    return chars.Count == 0 ? throw new InvalidOperationException($"{label} is required.") : new string(chars.ToArray());
}

static void ValidateEmail(string email)
{
    if (email.Length > 256) throw new InvalidOperationException("Platform administrator email is too long.");
    MailAddress parsed;
    try
    {
        parsed = new MailAddress(email);
    }
    catch (FormatException)
    {
        throw new InvalidOperationException("Platform administrator email is invalid.");
    }
    if (!string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Platform administrator email is invalid.");
}

static async Task VerifyCatalogIdentityAsync(HrmsCatalogDbContext catalog)
{
    var connection = catalog.Database.GetDbConnection();
    await connection.OpenAsync();
    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME();";
        var database = Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(database, "HRMS_Catalog", StringComparison.Ordinal))
            throw new InvalidOperationException($"Catalog database identity mismatch: expected HRMS_Catalog, received {database}.");
    }
    finally
    {
        await connection.CloseAsync();
    }
}

static async Task VerifyCatalogPrerequisitesAsync(HrmsCatalogDbContext catalog)
{
    var migrations = (await catalog.Database.GetAppliedMigrationsAsync()).ToArray();
    var expectedMigrations = new[]
    {
        "20260823113202_InitialCatalog",
        "20260905142921_AddTenantDatabaseProvider",
        "20260906130913_AddPlatformIdentity"
    };
    if (!migrations.SequenceEqual(expectedMigrations, StringComparer.Ordinal))
        throw new InvalidOperationException("Catalog migration prerequisites are not satisfied.");

    if (await catalog.PlatformUsers.CountAsync() != 0)
        throw new InvalidOperationException("PlatformUsers is not empty; refusing duplicate bootstrap.");

    if (await catalog.PlatformRoles.CountAsync(x => x.Name == PlatformRoleNames.PlatformSuperAdmin) != 1)
        throw new InvalidOperationException("PlatformSuperAdmin role prerequisite is not exactly present once.");

    var permissionCount = await catalog.PlatformPermissions.CountAsync(x => PlatformPermissions.All.Contains(x.Name));
    if (permissionCount != PlatformPermissions.All.Count)
        throw new InvalidOperationException("Platform permission prerequisites are incomplete.");

    var role = await catalog.PlatformRoles.SingleAsync(x => x.Name == PlatformRoleNames.PlatformSuperAdmin);
    var grantCount = await catalog.PlatformRolePermissions.CountAsync(x => x.PlatformRoleId == role.Id);
    if (grantCount != PlatformPermissions.All.Count)
        throw new InvalidOperationException("PlatformSuperAdmin must have exactly three grants before bootstrap.");
}

static async Task VerifyCreatedUserAsync(HrmsCatalogDbContext catalog, Guid userId, string normalizedEmail, int roleId)
{
    var user = await catalog.PlatformUsers.SingleAsync(x => x.Id == userId);
    if (!user.IsActive || user.SecurityRevision != 0 || user.NormalizedEmail != normalizedEmail || string.IsNullOrWhiteSpace(user.PasswordHash))
        throw new InvalidOperationException("Created PlatformUser verification failed.");
    if (await catalog.PlatformUserRoles.CountAsync(x => x.PlatformUserId == userId) != 1
        || !await catalog.PlatformUserRoles.AnyAsync(x => x.PlatformUserId == userId && x.PlatformRoleId == roleId))
        throw new InvalidOperationException("Created PlatformUser role verification failed.");
}

file sealed class BootstrapPlatformContext : IPlatformContext
{
    public Guid? UserId => null;
    public int? SecurityRevision => null;
    public bool HasSecurityRevision => false;
}
