using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BackupEncryptionPrototype;

public sealed record LocalRecoverySigningCertificateInfo(string Thumbprint, string PublicKeyPem, DateTime NotAfterUtc,
    string PrivateKeyProvider, string PrivateKeyAclStatus);

/// <summary>Creates a CurrentUser CNG ECDSA signing key with export disabled and a protected key-file ACL.</summary>
[SupportedOSPlatform("windows")]
public sealed class LocalRecoverySigningCertificateProvisioner : IDisposable
{
    private readonly string _keyName;
    private readonly string _thumbprint;
    private bool _persisted;
    private bool _disposed;
    public LocalRecoverySigningCertificateInfo Info { get; }

    private LocalRecoverySigningCertificateProvisioner(string keyName, string thumbprint, LocalRecoverySigningCertificateInfo info)
        => (_keyName, _thumbprint, Info) = (keyName, thumbprint, info);

    public static LocalRecoverySigningCertificateProvisioner CreateNew()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows CNG CurrentUser key storage is required.");
        var keyName = "HRMS-Local-Recovery-" + Guid.NewGuid().ToString("N");
        X509Certificate2? certificate = null;
        try
        {
            var parameters = new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Signing,
                KeyCreationOptions = CngKeyCreationOptions.None
            };
            using (var key = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, parameters))
            using (var signer = new ECDsaCng(key))
            {
                var request = new CertificateRequest("CN=HRMS Local Recovery Evidence", signer, HashAlgorithmName.SHA256);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                var now = DateTimeOffset.UtcNow;
                certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(2));
            }

            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                store.Add(certificate);
            }
            var thumbprint = NormalizeThumbprint(certificate.Thumbprint);
            var keyPath = GetCurrentUserKeyPath(keyName);
            RestrictedBackupAcl.ProtectFile(keyPath);
            RestrictedBackupAcl.VerifyFile(keyPath);

            using var currentStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            currentStore.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            using var stored = currentStore.Certificates.Cast<X509Certificate2>()
                .Single(c => NormalizeThumbprint(c.Thumbprint) == thumbprint);
            using var privateKey = stored.GetECDsaPrivateKey() ?? throw new InvalidOperationException("Stored signing certificate has no ECDSA private key.");
            if (privateKey is not ECDsaCng cng || cng.Key.ExportPolicy != CngExportPolicies.None || !stored.HasPrivateKey)
                throw new InvalidOperationException("Stored signing key is not a non-exportable CNG key.");
            var publicPem = stored.GetECDsaPublicKey()?.ExportSubjectPublicKeyInfoPem()
                ?? throw new InvalidOperationException("Stored signing certificate public key could not be exported.");
            var challenge = RandomNumberGenerator.GetBytes(32);
            try
            {
                var signature = privateKey.SignData(challenge, HashAlgorithmName.SHA256);
                if (!privateKey.VerifyData(challenge, signature, HashAlgorithmName.SHA256))
                    throw new CryptographicException("Protected signing-key self-test failed.");
                CryptographicOperations.ZeroMemory(signature);
            }
            finally { CryptographicOperations.ZeroMemory(challenge); }
            return new LocalRecoverySigningCertificateProvisioner(keyName, thumbprint,
                new(thumbprint, publicPem, stored.NotAfter.ToUniversalTime(), cng.Key.Provider?.Provider ?? "Microsoft Software Key Storage Provider", "restricted-operator-system-administrators"));
        }
        catch
        {
            if (certificate is not null)
            {
                try
                {
                    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                    store.Remove(certificate);
                }
                catch { }
                certificate.Dispose();
            }
            DeleteKeyIfPresent(keyName);
            throw;
        }
        finally { certificate?.Dispose(); }
    }

    /// <summary>Retains the uniquely created cert/key after its public-key pin has been saved securely.</summary>
    public void Persist()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _persisted = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_persisted) return;
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
            foreach (var certificate in store.Certificates.Cast<X509Certificate2>()
                         .Where(c => NormalizeThumbprint(c.Thumbprint) == _thumbprint).ToArray())
            {
                using (certificate) store.Remove(certificate);
            }
        }
        finally { DeleteKeyIfPresent(_keyName); }
    }

    private static string GetCurrentUserKeyPath(string keyName)
    {
        using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.None);
        if (key.ExportPolicy != CngExportPolicies.None || string.IsNullOrWhiteSpace(key.UniqueName))
            throw new InvalidOperationException("CNG key is exportable or has no persistent user-store identifier.");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Crypto", "Keys", key.UniqueName);
        if (!File.Exists(path)) throw new InvalidOperationException("CNG private-key file could not be located for ACL verification.");
        return path;
    }

    private static void DeleteKeyIfPresent(string keyName)
    {
        try
        {
            using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.None);
            key.Delete();
        }
        catch (CryptographicException) { }
        catch (FileNotFoundException) { }
    }

    private static string NormalizeThumbprint(string? value) => new string((value ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
}
