using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net;

namespace BackupEncryptionPrototype;

/// <summary>
/// Binary streaming archive for local-development recovery backups. Callers must use the
/// verified MySQL source and isolated restore workflow; production use is not permitted.
/// Format: authenticated fixed header, ordered AEAD chunks, authenticated terminal record.
/// </summary>
public static class StreamingBackupArchive
{
    private static readonly byte[] Magic = "HRMSBG01"u8.ToArray();
    private static readonly byte[] EndMagic = "HRMSEND1"u8.ToArray();
    private const int NoncePrefixLength = 4;
    private const int TagLength = 16;
    private const int HeaderLimit = 16 * 1024;
    public const int DefaultChunkSize = 1024 * 1024;
    public const int MaximumChunkSize = 4 * 1024 * 1024;

    public sealed record ArchiveScope(string SourceServerUuid, string TenantCode, string Database, string Host,
        int Port, string Catalog, string Provider, string TlsMode);
    public sealed record Metadata(string BackupId, int ChunkSize, byte[] NoncePrefix, string Purpose,
        string SourceServerUuid, string TenantCode, string Database, string Host, int Port, string Catalog, string Provider, string TlsMode);

    internal static void Encrypt(Stream plaintext, Stream encrypted, string backupId, int chunkSize = DefaultChunkSize, ArchiveScope? scope = null)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try { EncryptWithKey(plaintext, encrypted, backupId, key, chunkSize, scope); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal static void EncryptWithKey(Stream plaintext, Stream encrypted, string backupId, ReadOnlySpan<byte> dataKey, int chunkSize = DefaultChunkSize, ArchiveScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(encrypted);
        if (!plaintext.CanRead || !encrypted.CanWrite) throw new ArgumentException("Readable source and writable destination streams are required.");
        if (dataKey.Length != 32) throw new ArgumentException("AES-256 key required.", nameof(dataKey));
        if (string.IsNullOrWhiteSpace(backupId) || backupId.Length > 128) throw new ArgumentException("Backup identifier is invalid.", nameof(backupId));
        if (chunkSize is < 4096 or > MaximumChunkSize) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (scope is not null && (string.IsNullOrWhiteSpace(scope.SourceServerUuid) || scope.TenantCode != "WEB1" || scope.Database != "hrms_web1" ||
            scope.Catalog != "hrms_catalog" || scope.Provider != "MySql" || !IPAddress.TryParse(scope.Host, out var sourceIp) ||
            !IPAddress.IsLoopback(sourceIp) || scope.Port is < 1 or > 65535 || scope.TlsMode is not ("VerifyCA" or "VerifyFull")))
            throw new ArgumentException("Archive source scope is not the approved WEB1 database.", nameof(scope));

        var key = dataKey.ToArray();
        var prefix = RandomNumberGenerator.GetBytes(NoncePrefixLength);
        var header = JsonSerializer.SerializeToUtf8Bytes(new Metadata(backupId, chunkSize, prefix, "HRMS local recovery backup",
            scope?.SourceServerUuid ?? "synthetic", scope?.TenantCode ?? "SYNTHETIC", scope?.Database ?? "synthetic",
            scope?.Host ?? "127.0.0.1", scope?.Port ?? 3306, scope?.Catalog ?? "synthetic", scope?.Provider ?? "Synthetic", scope?.TlsMode ?? "VerifyCA"));
        var plain = new byte[chunkSize];
        var cipher = new byte[chunkSize];
        var tag = new byte[TagLength];
        try
        {
            encrypted.Write(Magic);
            WriteUInt32(encrypted, checked((uint)header.Length));
            encrypted.Write(header);
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(Nonce(prefix, 0), ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, header);
            encrypted.Write(tag);
            ulong sequence = 1;
            ulong total = 0;
            while (true)
            {
                var count = ReadChunk(plaintext, plain);
                if (count == 0) break;
                aes.Encrypt(Nonce(prefix, sequence), plain.AsSpan(0, count), cipher.AsSpan(0, count), tag,
                    ChunkAad(header, sequence, (uint)count));
                WriteUInt64(encrypted, sequence);
                WriteUInt32(encrypted, (uint)count);
                encrypted.Write(cipher, 0, count);
                encrypted.Write(tag);
                total = checked(total + (uint)count);
                sequence++;
                CryptographicOperations.ZeroMemory(plain.AsSpan(0, count));
                CryptographicOperations.ZeroMemory(cipher.AsSpan(0, count));
            }
            encrypted.Write(EndMagic);
            WriteUInt64(encrypted, sequence - 1);
            WriteUInt64(encrypted, total);
            aes.Encrypt(Nonce(prefix, sequence), ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag,
                EndAad(header, sequence, sequence - 1, total));
            encrypted.Write(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    /// <summary>Authenticates the complete artifact before any restore target is touched.</summary>
    public static Metadata Verify(Stream encrypted, ReadOnlySpan<byte> key, string expectedBackupId)
        => DecryptCore(encrypted, Stream.Null, key, expectedBackupId);

    /// <summary>Authenticates the full seekable artifact before emitting any plaintext.</summary>
    public static Metadata VerifyThenDecrypt(Stream encrypted, Stream plaintext, ReadOnlySpan<byte> key, string expectedBackupId)
    {
        if (!plaintext.CanWrite) throw new ArgumentException("Writable plaintext sink required.", nameof(plaintext));
        if (!encrypted.CanSeek) throw new ArgumentException("A seekable, locked artifact handle is required for verify-before-restore.", nameof(encrypted));
        var originalPosition = encrypted.Position;
        try
        {
            encrypted.Position = 0;
            var metadata = Verify(encrypted, key, expectedBackupId);
            encrypted.Position = 0;
            var secondPass = DecryptCore(encrypted, plaintext, key, expectedBackupId);
            if (metadata.BackupId != secondPass.BackupId || metadata.ChunkSize != secondPass.ChunkSize ||
                metadata.Purpose != secondPass.Purpose || metadata.SourceServerUuid != secondPass.SourceServerUuid ||
                metadata.TenantCode != secondPass.TenantCode || metadata.Database != secondPass.Database || metadata.Host != secondPass.Host ||
                metadata.Port != secondPass.Port || metadata.Catalog != secondPass.Catalog || metadata.Provider != secondPass.Provider || metadata.TlsMode != secondPass.TlsMode ||
                !metadata.NoncePrefix.AsSpan().SequenceEqual(secondPass.NoncePrefix))
                throw new InvalidDataException("Archive changed between authentication and restore pass.");
            return metadata;
        }
        finally { encrypted.Position = originalPosition; }
    }

    private static Metadata DecryptCore(Stream source, Stream destination, ReadOnlySpan<byte> key, string expectedBackupId)
    {
        if (!source.CanRead || key.Length != 32) throw new ArgumentException("Readable artifact and AES-256 key required.");
        if (!ReadExactly(source, Magic.Length).AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Archive magic/version is invalid.");
        var headerLength = ReadUInt32(source);
        if (headerLength is 0 or > HeaderLimit) throw new InvalidDataException("Archive header length is invalid.");
        var header = ReadExactly(source, checked((int)headerLength));
        var metadata = JsonSerializer.Deserialize<Metadata>(header) ?? throw new InvalidDataException("Archive metadata is invalid.");
        if (!string.Equals(metadata.BackupId, expectedBackupId, StringComparison.Ordinal) || metadata.NoncePrefix is not { Length: NoncePrefixLength } ||
            metadata.ChunkSize is < 4096 or > MaximumChunkSize || metadata.Purpose != "HRMS local recovery backup" ||
            string.IsNullOrWhiteSpace(metadata.SourceServerUuid) || string.IsNullOrWhiteSpace(metadata.TenantCode) || string.IsNullOrWhiteSpace(metadata.Database) ||
            !IPAddress.TryParse(metadata.Host, out var metadataIp) || !IPAddress.IsLoopback(metadataIp) || metadata.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(metadata.Catalog) || string.IsNullOrWhiteSpace(metadata.Provider) || metadata.TlsMode is not ("VerifyCA" or "VerifyFull"))
            throw new InvalidDataException("Archive metadata does not match the expected backup context.");
        var prefix = metadata.NoncePrefix;
        var headerTag = ReadExactly(source, TagLength);
        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(Nonce(prefix, 0), ReadOnlySpan<byte>.Empty, headerTag, Span<byte>.Empty, header);
        ulong nextSequence = 1;
        ulong total = 0;
        var plain = new byte[metadata.ChunkSize];
        try
        {
            while (true)
            {
                var marker = ReadExactly(source, 8);
                if (marker.AsSpan().SequenceEqual(EndMagic))
                {
                    var chunks = ReadUInt64(source);
                    var declaredTotal = ReadUInt64(source);
                    var endTag = ReadExactly(source, TagLength);
                    if (chunks != nextSequence - 1 || declaredTotal != total) throw new InvalidDataException("Archive terminal counts do not match.");
                    aes.Decrypt(Nonce(prefix, nextSequence), ReadOnlySpan<byte>.Empty, endTag, Span<byte>.Empty,
                        EndAad(header, nextSequence, chunks, declaredTotal));
                    if (source.ReadByte() != -1) throw new InvalidDataException("Trailing bytes after authenticated archive end.");
                    return metadata;
                }
                var actualSequence = BinaryPrimitives.ReadUInt64BigEndian(marker);
                var length = ReadUInt32(source);
                if (actualSequence != nextSequence || length == 0 || length > metadata.ChunkSize)
                    throw new InvalidDataException("Archive chunk order or length is invalid.");
                var encryptedChunk = ReadExactly(source, checked((int)length));
                var tag = ReadExactly(source, TagLength);
                aes.Decrypt(Nonce(prefix, nextSequence), encryptedChunk, tag, plain.AsSpan(0, (int)length),
                    ChunkAad(header, nextSequence, length));
                destination.Write(plain, 0, (int)length);
                total = checked(total + length);
                nextSequence++;
                CryptographicOperations.ZeroMemory(plain.AsSpan(0, (int)length));
                CryptographicOperations.ZeroMemory(encryptedChunk);
                CryptographicOperations.ZeroMemory(tag);
            }
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Archive is truncated or missing its authenticated end record.", ex); }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] Nonce(byte[] prefix, ulong sequence)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), sequence);
        return nonce;
    }

    private static byte[] ChunkAad(byte[] header, ulong sequence, uint length)
    {
        var aad = new byte[header.Length + 12];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(header.Length), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(header.Length + 8), length);
        return aad;
    }

    private static byte[] EndAad(byte[] header, ulong seq, ulong count, ulong total)
    {
        var aad = new byte[header.Length + 24];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(header.Length), seq);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(header.Length + 8), count);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(header.Length + 16), total);
        return aad;
    }

    private static int ReadChunk(Stream source, byte[] buffer)
    {
        var count = 0;
        while (count < buffer.Length)
        {
            var read = source.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        return count;
    }

    private static byte[] ReadExactly(Stream stream, int length)
    {
        var bytes = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(bytes, offset, length - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }
    private static uint ReadUInt32(Stream s) { Span<byte> b = stackalloc byte[4]; ReadExactly(s, b); return BinaryPrimitives.ReadUInt32BigEndian(b); }
    private static ulong ReadUInt64(Stream s) { Span<byte> b = stackalloc byte[8]; ReadExactly(s, b); return BinaryPrimitives.ReadUInt64BigEndian(b); }
    private static void ReadExactly(Stream s, Span<byte> b) { var offset = 0; while (offset < b.Length) { var n = s.Read(b[offset..]); if (n == 0) throw new EndOfStreamException(); offset += n; } }
    private static void WriteUInt32(Stream s, uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); s.Write(b); }
    private static void WriteUInt64(Stream s, ulong value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); s.Write(b); }
}
