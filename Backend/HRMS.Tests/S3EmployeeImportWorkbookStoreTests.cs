using System.Security.Cryptography;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Storage;
using HRMS.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HRMS.Tests;

public sealed class S3EmployeeImportWorkbookStoreTests
{
    private static readonly Guid TenantId = SeedData.TenantIds.Demo01;
    private static readonly Guid UserId = SeedData.Users.First(x => x.TenantId == TenantId).Id;
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Upload_download_is_encrypted_and_survives_key_rotation()
    {
        var objectClient = new FakeObjectClient();
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var originalSettings = Settings("key-v1", oldKey);
        var first = CreateStore(originalSettings, objectClient, TenantId);
        var bytes = "confidential S3 workbook"u8.ToArray();
        var id = Guid.NewGuid();
        var hash = Hash(bytes);
        await Save(first, id, bytes, hash);
        Assert.True(await first.IsAvailableAsync(id));
        Assert.DoesNotContain(bytes, objectClient.Objects.Values.Single());

        var rotatedSettings = Settings("key-v2", RandomNumberGenerator.GetBytes(32));
        rotatedSettings["EmployeeImportStorage:Encryption:Keys:key-v1"] = Convert.ToBase64String(oldKey);
        var afterRotation = CreateStore(rotatedSettings, objectClient, TenantId);
        await using var decrypted = await afterRotation.OpenReadAsync(id, hash);
        Assert.NotNull(decrypted);
        using var output = new MemoryStream();
        await decrypted!.CopyToAsync(output);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public async Task Tenant_scoping_hash_verification_and_missing_object_are_enforced()
    {
        var objects = new FakeObjectClient();
        var bytes = new byte[] { 4, 5, 6 };
        var id = Guid.NewGuid();
        var hash = Hash(bytes);
        var tenantStore = CreateStore(Settings("v1", RandomNumberGenerator.GetBytes(32)), objects, TenantId);
        Assert.Null(await tenantStore.OpenReadAsync(id, hash));
        Assert.False(await tenantStore.IsAvailableAsync(id));
        await Assert.ThrowsAsync<InvalidDataException>(() => Save(tenantStore, id, bytes, new string('0', 64)));
        await Save(tenantStore, id, bytes, hash);

        var otherTenant = SeedData.TenantIds.Demo02;
        var foreign = CreateStore(Settings("v1", RandomNumberGenerator.GetBytes(32)), objects, otherTenant);
        Assert.Null(await foreign.OpenReadAsync(id, hash));
        Assert.False(await foreign.IsAvailableAsync(id));
    }

    [Fact]
    public async Task Tampered_object_and_duplicate_object_are_rejected()
    {
        var objects = new FakeObjectClient();
        var store = CreateStore(Settings("v1", RandomNumberGenerator.GetBytes(32)), objects, TenantId);
        var bytes = new byte[] { 11, 12, 13 };
        var id = Guid.NewGuid();
        var hash = Hash(bytes);
        await Save(store, id, bytes, hash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Save(store, id, bytes, hash));
        var objectKey = Assert.Single(objects.Objects.Keys);
        objects.Objects[objectKey][^1] ^= 0x20;
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenReadAsync(id, hash));
    }

    [Fact]
    public async Task Retention_deletion_is_idempotent_and_respects_retention_window()
    {
        var objects = new FakeObjectClient();
        var store = CreateStore(Settings("v1", RandomNumberGenerator.GetBytes(32)), objects, TenantId);
        var bytes = new byte[] { 2, 2, 2 };
        var id = Guid.NewGuid();
        await Save(store, id, bytes);
        Assert.False(await store.DeleteExpiredAsync(id, Now.UtcDateTime.AddDays(-2)));
        Assert.True(await store.DeleteExpiredAsync(id, Now.UtcDateTime.AddDays(-31)));
        Assert.False(await store.DeleteExpiredAsync(id, Now.UtcDateTime.AddDays(-31)));
        Assert.False(await store.IsAvailableAsync(id));
    }

    [Fact]
    public void Storage_configuration_selects_provider_and_rejects_invalid_production_transport()
    {
        var settings = Settings("v1", RandomNumberGenerator.GetBytes(32));
        var options = EmployeeImportStorageOptions.LoadAndValidate(Configuration(settings), new TestHostEnvironment("Production"));
        Assert.Equal(EmployeeImportStorageProvider.S3, options.Provider);
        Assert.Equal("hrms-private-workbooks", options.Bucket);
        settings["EmployeeImportStorage:S3:Endpoint"] = "http://object-store:9000";
        settings["EmployeeImportStorage:S3:UseTls"] = "false";
        Assert.Throws<InvalidOperationException>(() => EmployeeImportStorageOptions.LoadAndValidate(
            Configuration(settings), new TestHostEnvironment("Production")));
    }

    [Fact]
    public void Configuration_accepts_only_explicit_development_http_and_external_credentials()
    {
        var settings = Settings("v1", RandomNumberGenerator.GetBytes(32));
        settings["EmployeeImportStorage:S3:Endpoint"] = "http://minio:9000";
        settings["EmployeeImportStorage:S3:UseTls"] = "false";
        settings["EmployeeImportStorage:S3:AllowInsecureDevelopmentEndpoint"] = "true";
        var development = EmployeeImportStorageOptions.LoadAndValidate(Configuration(settings), new TestHostEnvironment("Development"));
        Assert.False(development.UseTls);
        Assert.Throws<InvalidOperationException>(() => EmployeeImportStorageOptions.LoadAndValidate(
            Configuration(settings), new TestHostEnvironment("Production")));

        settings["EmployeeImportStorage:S3:Credentials:Mode"] = "Static";
        Assert.Throws<InvalidOperationException>(() => EmployeeImportStorageOptions.LoadAndValidate(
            Configuration(settings), new TestHostEnvironment("Development")));
        settings["EmployeeImportStorage:S3:Credentials:AccessKey"] = "provided-outside-source";
        settings["EmployeeImportStorage:S3:Credentials:SecretKey"] = "provided-outside-source";
        Assert.Equal(EmployeeImportStorageCredentialMode.Static,
            EmployeeImportStorageOptions.LoadAndValidate(Configuration(settings), new TestHostEnvironment("Development")).CredentialMode);
    }

    [Fact]
    public async Task Missing_key_and_cross_tenant_context_fail_closed()
    {
        var missingKey = Settings("v1", RandomNumberGenerator.GetBytes(32));
        missingKey.Remove("EmployeeImportStorage:Encryption:Keys:v1");
        var store = CreateStore(missingKey, new FakeObjectClient(), TenantId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Save(store, Guid.NewGuid(), [1, 2]));

        var noTenant = CreateStore(Settings("v1", RandomNumberGenerator.GetBytes(32)), new FakeObjectClient(), Guid.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Save(noTenant, Guid.NewGuid(), [1, 2]));
    }

    private static S3EmployeeImportWorkbookStore CreateStore(Dictionary<string, string?> settings, FakeObjectClient objects, Guid tenantId)
    {
        var configuration = Configuration(settings);
        var tenant = tenantId == Guid.Empty ? new TestTenantContext(null, null) : new TestTenantContext(tenantId, SeedData.Users.First(x => x.TenantId == tenantId).Id);
        return new S3EmployeeImportWorkbookStore(configuration, new TestHostEnvironment("Development"), tenant,
            new FixedClock(Now), new EmployeeImportWorkbookCipher(configuration), objects);
    }

    private static Dictionary<string, string?> Settings(string keyId, byte[] key) => new()
    {
        ["EmployeeImportStorage:Provider"] = "S3",
        ["EmployeeImportStorage:RetentionDays"] = "30",
        ["EmployeeImportStorage:Encryption:ActiveKeyId"] = keyId,
        [$"EmployeeImportStorage:Encryption:Keys:{keyId}"] = Convert.ToBase64String(key),
        ["EmployeeImportStorage:S3:Bucket"] = "hrms-private-workbooks",
        ["EmployeeImportStorage:S3:Endpoint"] = "https://s3.example.test",
        ["EmployeeImportStorage:S3:Region"] = "us-east-1",
        ["EmployeeImportStorage:S3:UseTls"] = "true",
        ["EmployeeImportStorage:S3:Credentials:Mode"] = "DefaultChain"
    };

    private static IConfiguration Configuration(Dictionary<string, string?> settings) => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static Task Save(IEmployeeImportWorkbookStore store, Guid id, byte[] bytes, string? hash = null)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return store.SaveAsync(id, stream, hash ?? Hash(bytes));
    }

    private sealed class FakeObjectClient : IEmployeeImportObjectClient
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        private static string Id(string bucket, string key) => bucket + "/" + key;
        public Task PutIfAbsentAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Objects.TryAdd(Id(bucket, key), content.ToArray())) throw new InvalidOperationException("Workbook artifact already exists for this operation.");
            return Task.CompletedTask;
        }
        public Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Objects.TryGetValue(Id(bucket, key), out var bytes) ? bytes.ToArray() : null);
        }
        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Objects.ContainsKey(Id(bucket, key)));
        }
        public Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Objects.Remove(Id(bucket, key)); return Task.CompletedTask;
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
