using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using HRMS.Application.Services;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Security;
using HRMS.Infrastructure.Sharding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Wires a real <see cref="AuthService"/> against the shared in-memory SQLite database with real
/// password hashing and real JWT signing — only the clock, the ambient tenant and the resolved
/// organization are substitutable.
/// <para>
/// Each <see cref="CreateService"/> call builds a fresh DbContext <em>and a fresh shard context</em>,
/// mirroring the scoped lifetime a request would get, so tests can change the ambient tenant and the
/// organization between calls the way successive HTTP requests do. A shard context is write-once, so
/// reusing one across calls would make the second selection throw rather than switch.
/// </para>
/// </summary>
public sealed class AuthTestHarness : IDisposable
{
    /// <summary>The seeded organizations' own addresses, as the catalog records them.</summary>
    public const string Demo01Host = TestShards.Demo01Host;

    public const string Demo02Host = TestShards.Demo02Host;

    private readonly List<HrmsDbContext> _contexts = [];

    private AuthTestHarness(SqliteInMemoryDatabase database)
    {
        Database = database;
        TenantContext = new TestTenantContext();
        PasswordHasher = new IdentityPasswordHasher();
        JwtSettings = new JwtSettings
        {
            Issuer = "HRMS.Tests",
            Audience = "HRMS.Tests.Client",
            SecretKey = "test-only-signing-key-that-is-long-enough-for-hmac-sha256!!",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 7,
            ClockSkewSeconds = 0
        };
        TokenService = new JwtTokenService(Options.Create(JwtSettings), TimeProvider.System);
    }

    public SqliteInMemoryDatabase Database { get; }

    /// <summary>The ambient tenant/user. Null on both counts represents an unauthenticated request.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>
    /// The organization the next service's requests are addressed to, as host resolution would have set it.
    /// Null means a host no organization signs in at — the apex, or an address nobody has bought.
    /// </summary>
    public ShardDescriptor? CurrentShard { get; private set; }

    public JwtSettings JwtSettings { get; }
    public IJwtTokenService TokenService { get; }
    public IPasswordHasher PasswordHasher { get; }

    public static async Task<AuthTestHarness> CreateAsync()
    {
        var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        return new AuthTestHarness(database);
    }

    /// <summary>
    /// Addresses the next service to a seeded organization's own host, building the descriptor from the same
    /// seed rows the catalog holds — so what a test works with is what host resolution would produce. Takes
    /// the host rather than the organization code, because the host is what decides.
    /// </summary>
    public AuthTestHarness At(string host)
    {
        CurrentShard = TestShards.For(host);
        return this;
    }

    /// <summary>Addresses the next service to a host that resolves to no organization.</summary>
    public AuthTestHarness AtAnUnknownHost()
    {
        CurrentShard = null;
        return this;
    }

    /// <summary>
    /// Addresses the next service to a host the catalog routes somewhere its own database has no row for —
    /// an organization that was registered but never provisioned.
    /// </summary>
    public AuthTestHarness AtAnUnprovisionedOrganization()
    {
        CurrentShard = TestShards.Unprovisioned;
        return this;
    }

    /// <summary>Creates an AuthService on a fresh DbContext scoped to the current ambient tenant.</summary>
    public AuthService CreateService()
    {
        var context = Database.CreateContext(TenantContext);
        _contexts.Add(context);

        return CreateService(context);
    }

    /// <summary>
    /// Creates an AuthService over a caller-supplied context. Sharing one context between two services is
    /// not how the app runs, but it is the only way to reproduce a stale read deterministically.
    /// </summary>
    public AuthService CreateService(HrmsDbContext context) =>
        new(
            context,
            PasswordHasher,
            TokenService,
            TenantContext,
            BuildShardContext(),
            Options.Create(JwtSettings),
            TimeProvider.System,
            NullLogger<AuthService>.Instance);

    /// <summary>
    /// The real scoped implementation, one per service, selected exactly as the middleware selects it.
    /// A stand-in would be free to allow a mid-scope switch that production refuses.
    /// </summary>
    private IShardContext BuildShardContext()
    {
        var shardContext = new ShardContext();
        if (CurrentShard is not null)
        {
            shardContext.Use(CurrentShard);
        }

        return shardContext;
    }

    /// <summary>A context that sees every tenant's rows, for arranging and asserting directly.</summary>
    public HrmsDbContext CreateUnscopedContext()
    {
        var context = Database.CreateContext(new TestTenantContext());
        _contexts.Add(context);
        return context;
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        Database.Dispose();
    }
}
