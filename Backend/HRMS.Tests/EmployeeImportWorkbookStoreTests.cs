using System.Security.Cryptography;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Storage;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HRMS.Tests;

public sealed class EmployeeImportWorkbookStoreTests : IDisposable
{
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), "hrms-import-store-tests", Guid.NewGuid().ToString("N"));
    private static readonly Guid TenantId = SeedData.TenantIds.Demo01;
    private static readonly Guid UserId = SeedData.Users.First(x => x.TenantId == TenantId).Id;
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_storage_configuration_and_missing_key_fail_closed()
    {
        var tenant = new TestTenantContext(TenantId, UserId);
        var missingPath = CreateStore(new Dictionary<string, string?>(), tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(missingPath, Guid.NewGuid(), [1, 2, 3]));

        var missingKey = CreateStore(new Dictionary<string, string?> { ["EmployeeImportStorage:RootPath"] = PrivateRoot }, tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(missingKey, Guid.NewGuid(), [1, 2, 3]));
    }

    [Fact]
    public async Task Workbook_is_encrypted_authenticated_and_survives_provider_restart()
    {
        var settings = ValidSettings();
        var tenant = new TestTenantContext(TenantId, UserId);
        var bytes = "sensitive workbook contents"u8.ToArray();
        var operationId = Guid.NewGuid();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var first = CreateStore(settings, tenant);
        await SaveAsync(first, operationId, bytes, hash);
        Assert.True(await first.IsAvailableAsync(operationId));
        var files = Directory.GetFiles(PrivateRoot, "*.hrw", SearchOption.AllDirectories);
        var encrypted = await File.ReadAllBytesAsync(Assert.Single(files));
        Assert.DoesNotContain(bytes, encrypted);

        var restartedProvider = CreateStore(settings, new TestTenantContext(TenantId, UserId));
        await using var recovered = await restartedProvider.OpenReadAsync(operationId, hash);
        Assert.NotNull(recovered);
        using var output = new MemoryStream();
        await recovered!.CopyToAsync(output);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public async Task Authentication_tag_tampering_and_corrupt_artifact_are_rejected()
    {
        var settings = ValidSettings();
        var store = CreateStore(settings, new TestTenantContext(TenantId, UserId));
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var id = Guid.NewGuid();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await SaveAsync(store, id, bytes, hash);
        var path = Assert.Single(Directory.GetFiles(PrivateRoot, "*.hrw", SearchOption.AllDirectories));
        var data = await File.ReadAllBytesAsync(path);
        data[4 + 1 + "test-v1".Length + 12] ^= 0x40;
        await File.WriteAllBytesAsync(path, data);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenReadAsync(id, hash));

        await File.WriteAllBytesAsync(path, [0x00, 0x01]);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenReadAsync(id, hash));
    }

    [Fact]
    public async Task Hash_mismatch_and_cross_tenant_access_are_rejected()
    {
        var settings = ValidSettings();
        var store = CreateStore(settings, new TestTenantContext(TenantId, UserId));
        var bytes = new byte[] { 7, 8, 9 };
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidDataException>(() => SaveAsync(store, id, bytes, new string('0', 64)));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await SaveAsync(store, id, bytes, hash);

        var otherTenant = SeedData.TenantIds.Demo02;
        var foreignUser = SeedData.Users.First(x => x.TenantId == otherTenant).Id;
        var foreignStore = CreateStore(settings, new TestTenantContext(otherTenant, foreignUser));
        Assert.False(await foreignStore.IsAvailableAsync(id));
        Assert.Null(await foreignStore.OpenReadAsync(id, hash));
    }

    [Fact]
    public async Task Storage_root_inside_web_root_is_rejected_and_client_names_cannot_select_paths()
    {
        var tenant = new TestTenantContext(TenantId, UserId);
        var webRootSettings = ValidSettings(Path.Combine(ContentRoot, "wwwroot", "employee-imports"));
        var webRootStore = CreateStore(webRootSettings, tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(webRootStore, Guid.NewGuid(), [1]));

        var privateStore = CreateStore(ValidSettings(), tenant);
        var id = Guid.NewGuid();
        var bytes = new byte[] { 3, 2, 1 };
        await SaveAsync(privateStore, id, bytes);
        var expected = Path.Combine(PrivateRoot, TenantId.ToString("N"), id.ToString("N") + ".hrw");
        Assert.True(File.Exists(expected));
        Assert.DoesNotContain("..", expected);
    }

    [Fact]
    public async Task Retention_deletion_is_refused_until_expired_then_artifact_is_unavailable()
    {
        var store = CreateStore(ValidSettings(), new TestTenantContext(TenantId, UserId));
        var id = Guid.NewGuid();
        var bytes = new byte[] { 8, 8, 8 };
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await SaveAsync(store, id, bytes, hash);
        Assert.False(await store.DeleteExpiredAsync(id, Now.UtcDateTime.AddDays(-1)));
        Assert.True(await store.DeleteExpiredAsync(id, Now.UtcDateTime.AddDays(-31)));
        Assert.False(await store.IsAvailableAsync(id));
    }

    [Fact]
    public async Task Invalid_key_length_and_missing_active_key_fail_closed()
    {
        var tenant = new TestTenantContext(TenantId, UserId);
        var invalidLength = ValidSettings();
        invalidLength["EmployeeImportStorage:Encryption:Keys:test-v1"] = Convert.ToBase64String(new byte[16]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(CreateStore(invalidLength, tenant), Guid.NewGuid(), [1]));

        var missingActive = ValidSettings();
        missingActive["EmployeeImportStorage:Encryption:ActiveKeyId"] = "missing-v2";
        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(CreateStore(missingActive, tenant), Guid.NewGuid(), [1]));
    }

    private string ContentRoot => Path.Combine(sandbox, "content");
    private string PrivateRoot => Path.Combine(sandbox, "private-workbooks");

    private Dictionary<string, string?> ValidSettings(string? root = null) => new()
    {
        ["EmployeeImportStorage:RootPath"] = root ?? PrivateRoot,
        ["EmployeeImportStorage:RetentionDays"] = "30",
        ["EmployeeImportStorage:Encryption:ActiveKeyId"] = "test-v1",
        ["EmployeeImportStorage:Encryption:Keys:test-v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    };

    private EncryptedFileEmployeeImportWorkbookStore CreateStore(Dictionary<string, string?> settings, ITenantContext tenant)
    {
        Directory.CreateDirectory(ContentRoot);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new EncryptedFileEmployeeImportWorkbookStore(configuration,
            new TestHostEnvironment(ContentRoot), tenant, new FixedClock(Now), new EmployeeImportWorkbookCipher(configuration));
    }

    private static Task SaveAsync(IEmployeeImportWorkbookStore store, Guid id, byte[] bytes, string? hash = null)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return store.SaveAsync(id, stream, hash ?? Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public void Dispose()
    {
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
