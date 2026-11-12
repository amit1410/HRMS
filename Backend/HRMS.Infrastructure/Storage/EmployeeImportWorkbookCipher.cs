using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace HRMS.Infrastructure.Storage;

/// <summary>AES-256-GCM envelope with versioned metadata and tenant/operation-bound associated data.</summary>
public sealed class EmployeeImportWorkbookCipher(IConfiguration configuration) : IEmployeeImportWorkbookCipher
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MaximumKeyIdLength = 64;
    private static readonly byte[] Magic = "HRW1"u8.ToArray();
    private static readonly byte[] Context = "HRMS-EMPLOYEE-IMPORT-WORKBOOK-V1"u8.ToArray();
    private static readonly Regex KeyIdPattern = new("\\A[A-Za-z0-9._-]{1,64}\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public byte[] Encrypt(Guid tenantId, Guid operationId, ReadOnlySpan<byte> plaintext)
    {
        var ring = LoadKeyRing();
        var keyIdBytes = Encoding.UTF8.GetBytes(ring.ActiveKeyId);
        var key = ring.Keys[ring.ActiveKeyId];
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        var aad = AssociatedData(tenantId, operationId, keyIdBytes);
        try
        {
            using (var aes = new AesGcm(key, TagLength)) aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            var envelope = new byte[Magic.Length + 1 + keyIdBytes.Length + NonceLength + TagLength + ciphertext.Length];
            var offset = 0;
            Magic.CopyTo(envelope, offset); offset += Magic.Length;
            envelope[offset++] = checked((byte)keyIdBytes.Length);
            keyIdBytes.CopyTo(envelope, offset); offset += keyIdBytes.Length;
            nonce.CopyTo(envelope, offset); offset += NonceLength;
            tag.CopyTo(envelope, offset); offset += TagLength;
            ciphertext.CopyTo(envelope, offset);
            return envelope;
        }
        finally
        {
            Clear(ring.Keys);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    public byte[] Decrypt(Guid tenantId, Guid operationId, ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length < Magic.Length + 1 + 1 + NonceLength + TagLength || !envelope[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Stored workbook artifact is malformed.");
        var keyIdLength = envelope[Magic.Length];
        var keyStart = Magic.Length + 1;
        if (keyIdLength is 0 or > MaximumKeyIdLength || envelope.Length < keyStart + keyIdLength + NonceLength + TagLength)
            throw new InvalidDataException("Stored workbook artifact is malformed.");
        var keyId = Encoding.UTF8.GetString(envelope.Slice(keyStart, keyIdLength));
        if (!KeyIdPattern.IsMatch(keyId)) throw new InvalidDataException("Stored workbook artifact is malformed.");

        var ring = LoadKeyRing();
        if (!ring.Keys.TryGetValue(keyId, out var key))
        {
            Clear(ring.Keys);
            throw new InvalidOperationException("The encryption key required by this workbook is not configured.");
        }
        var nonceStart = keyStart + keyIdLength;
        var tagStart = nonceStart + NonceLength;
        var ciphertextStart = tagStart + TagLength;
        var plaintext = new byte[envelope.Length - ciphertextStart];
        var aad = AssociatedData(tenantId, operationId, Encoding.UTF8.GetBytes(keyId));
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(envelope.Slice(nonceStart, NonceLength), envelope[ciphertextStart..],
                envelope.Slice(tagStart, TagLength), plaintext, aad);
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("Stored workbook artifact failed authentication.", exception);
        }
        finally
        {
            Clear(ring.Keys);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    private (string ActiveKeyId, Dictionary<string, byte[]> Keys) LoadKeyRing()
    {
        var active = configuration["EmployeeImportStorage:Encryption:ActiveKeyId"];
        if (string.IsNullOrWhiteSpace(active) || !KeyIdPattern.IsMatch(active))
            throw new InvalidOperationException("EmployeeImportStorage:Encryption:ActiveKeyId must identify a configured encryption key.");
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var child in configuration.GetSection("EmployeeImportStorage:Encryption:Keys").GetChildren())
        {
            if (!KeyIdPattern.IsMatch(child.Key) || string.IsNullOrWhiteSpace(child.Value))
            {
                Clear(keys);
                throw new InvalidOperationException("EmployeeImportStorage encryption key configuration is invalid.");
            }
            byte[] key;
            try { key = Convert.FromBase64String(child.Value); }
            catch (FormatException)
            {
                Clear(keys);
                throw new InvalidOperationException("EmployeeImportStorage encryption keys must be Base64-encoded 32-byte keys.");
            }
            if (key.Length != 32)
            {
                CryptographicOperations.ZeroMemory(key);
                Clear(keys);
                throw new InvalidOperationException("EmployeeImportStorage encryption keys must be Base64-encoded 32-byte keys.");
            }
            keys.Add(child.Key, key);
        }
        if (!keys.ContainsKey(active))
        {
            Clear(keys);
            throw new InvalidOperationException("The active EmployeeImportStorage encryption key is missing from the configured key ring.");
        }
        return (active, keys);
    }

    private static byte[] AssociatedData(Guid tenantId, Guid operationId, byte[] keyId) =>
        [.. Context, .. tenantId.ToByteArray(), .. operationId.ToByteArray(), .. keyId];

    private static void Clear(Dictionary<string, byte[]> keys)
    {
        foreach (var key in keys.Values) CryptographicOperations.ZeroMemory(key);
    }
}
