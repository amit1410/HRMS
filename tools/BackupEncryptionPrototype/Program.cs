using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupEncryptionPrototype;

internal static class Program
{
    public static int Main()
    {
        try { PrototypeTests.RunAll(); Console.WriteLine("BackupEncryptionPrototype: all synthetic tests passed."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine($"BackupEncryptionPrototype: test failure ({ex.GetType().Name}): {ex.Message}"); return 1; }
    }
}

public sealed record SyntheticFile(string RelativePath, byte[] Content);

// Local-development key-wrapping candidate. This deliberately uses user-scoped DPAPI
// (CRYPTPROTECT_UI_FORBIDDEN; CRYPTPROTECT_LOCAL_MACHINE is never set). It is not a
// portable or disaster-recovery key store and remains subject to independent review.
[SupportedOSPlatform("windows")]
public static class WindowsDpapiKeyProtector
{
    private const uint UiForbidden = 0x1;
    private static readonly byte[] ContextPrefix = "HRMS local backup data key v1:"u8.ToArray();

    public static byte[] Protect(ReadOnlySpan<byte> key, string backupId)
        => Transform(key, backupId, protect: true);

    public static byte[] Unprotect(ReadOnlySpan<byte> protectedKey, string backupId)
        => Transform(protectedKey, backupId, protect: false);

    private static byte[] Transform(ReadOnlySpan<byte> input, string backupId, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI current-user protection is required.");
        if (string.IsNullOrWhiteSpace(backupId) || backupId.Length > 128) throw new ArgumentException("A bounded backup identifier is required.", nameof(backupId));
        if (protect && input.Length != 32) throw new ArgumentException("A 256-bit AES data key is required.", nameof(input));
        var inputBytes = input.ToArray();
        var entropyText = ContextPrefix.Concat(System.Text.Encoding.UTF8.GetBytes(backupId)).ToArray();
        var entropyBytes = SHA256.HashData(entropyText);
        var inputBlob = Allocate(inputBytes);
        var entropyBlob = Allocate(entropyBytes);
        DataBlob outputBlob = default;
        IntPtr description = IntPtr.Zero;
        try
        {
            var success = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, out description, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outputBlob);
            if (!success) throw new CryptographicException($"Windows DPAPI operation failed with error {Marshal.GetLastWin32Error()}.");
            var result = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, result, 0, result.Length);
            if (!protect && result.Length != 32)
            {
                CryptographicOperations.ZeroMemory(result);
                throw new CryptographicException("Protected data did not contain a 256-bit AES key.");
            }
            return result;
        }
        finally
        {
            ZeroAndFree(inputBlob);
            ZeroAndFree(entropyBlob);
            if (outputBlob.Data != IntPtr.Zero) _ = LocalFree(outputBlob.Data);
            if (description != IntPtr.Zero) _ = LocalFree(description);
            CryptographicOperations.ZeroMemory(inputBytes);
            CryptographicOperations.ZeroMemory(entropyText);
            CryptographicOperations.ZeroMemory(entropyBytes);
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = pointer };
    }

    private static void ZeroAndFree(DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero) return;
        Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
        Marshal.FreeHGlobal(blob.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

public static class BackupPackage
{
    private static readonly byte[] Magic = "HREB"u8.ToArray(), EndMagic = "HREE"u8.ToArray();
    private const byte Version = 2;
    private const int KeySize = 32, NonceSize = 12, TagSize = 16;
    private const int HeaderSize = 4 + 1 + 1 + 4 + 8 + 8 + 16 + NonceSize;
    public const int MaxManifestBytes = 1 * 1024 * 1024, MaxFileCount = 100_000, MaxChunkSize = 4 * 1024 * 1024;
    public const ulong MaxFileBytes = 1UL * 1024 * 1024 * 1024, MaxTotalPlaintextBytes = 4UL * 1024 * 1024 * 1024, MaxChunkCount = 1_000_000;
    private const int MaxAllocation = 4 * 1024 * 1024;

    // The supported backup workflow generates a key inside this method. The returned key is
    // custody material; callers must destroy it after handing it to their approved custodian.
    public static byte[] EncryptWithFreshKey(Stream destination, IReadOnlyList<SyntheticFile> files, int chunkSize = 64 * 1024)
    {
        var key = RandomNumberGenerator.GetBytes(KeySize);
        EncryptCore(destination, files, key, chunkSize);
        return key;
    }

    // Atomic file workflow. The caller must provide a dedicated, access-controlled local
    // staging directory. It must be the final file's parent, so the commit is one-volume
    // and one-directory File.Move. The final path is never overwritten.
    public static byte[] EncryptToFile(string destinationPath, IReadOnlyList<SyntheticFile> files, string approvedStagingDirectory, int chunkSize = 64 * 1024)
    {
        ValidateDestinationFile(destinationPath, approvedStagingDirectory);
        var destination = Path.GetFullPath(destinationPath);
        var stagingDirectory = Path.GetFullPath(approvedStagingDirectory);
        var staging = Path.Combine(stagingDirectory, ".backup-encryption-" + Guid.NewGuid().ToString("N") + ".tmp");
        var key = RandomNumberGenerator.GetBytes(KeySize);
        Exception? failure = null;
        try
        {
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                EncryptCore(output, files, key, chunkSize);
                output.Flush(true);
            }

            // Re-open only after close and authenticate the complete package before commit.
            using (var verification = new FileStream(staging, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
                _ = DecryptToStreamCore(verification, Stream.Null, key);

            // File.Move without overwrite is the commit. A concurrent creator causes a
            // failure and leaves no final output.
            File.Move(staging, destination);
            return key;
        }
        catch (Exception ex)
        {
            failure = ex;
            try { File.Delete(staging); }
            catch (Exception cleanup) { throw new IOException("Encryption failed and staging cleanup also failed; output was not committed.", new AggregateException(failure, cleanup)); }
            throw new IOException("Encryption failed; no output was committed.", failure);
        }
    }

    // Local Windows candidate: encrypts first, wraps the one-time AES key with user-scoped
    // DPAPI entropy bound to backupId, and commits the wrapped key without overwriting.
    // The caller must still enforce ACLs on approvedStagingDirectory and obtain security review.
    [SupportedOSPlatform("windows")]
    public static ProtectedBackupArtifact EncryptToFileWithCurrentUserKey(
        string destinationPath, IReadOnlyList<SyntheticFile> files, string approvedStagingDirectory, string backupId, int chunkSize = 64 * 1024)
    {
        ValidateDestinationFile(destinationPath, approvedStagingDirectory);
        if (string.IsNullOrWhiteSpace(backupId) || backupId.Length > 128) throw new ArgumentException("A bounded backup identifier is required.", nameof(backupId));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The local key-protection workflow requires Windows DPAPI.");
        var encryptedPath = Path.GetFullPath(destinationPath);
        var wrappedKeyPath = encryptedPath + ".dpapi-key";
        if (File.Exists(wrappedKeyPath)) throw new IOException("Wrapped key destination already exists; overwrite refused.");
        byte[]? key = null;
        byte[]? protectedKey = null;
        var artifactCommitted = false;
        var wrappedKeyCommitted = false;
        var stagingKeyPath = Path.Combine(Path.GetFullPath(approvedStagingDirectory), ".backup-key-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            key = EncryptToFile(encryptedPath, files, approvedStagingDirectory, chunkSize);
            artifactCommitted = true;
            protectedKey = WindowsDpapiKeyProtector.Protect(key, backupId);
            using (var output = new FileStream(stagingKeyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                output.Write(protectedKey);
                output.Flush(true);
            }
            File.Move(stagingKeyPath, wrappedKeyPath);
            wrappedKeyCommitted = true;
            using var artifact = File.OpenRead(encryptedPath);
            return new(backupId, encryptedPath, wrappedKeyPath, Convert.ToHexString(SHA256.HashData(artifact)), "AES-256-GCM", "Windows-DPAPI-CurrentUser");
        }
        catch (Exception failure)
        {
            var cleanupFailures = new List<Exception>();
            try { if (File.Exists(stagingKeyPath)) File.Delete(stagingKeyPath); } catch (Exception ex) { cleanupFailures.Add(ex); }
            try { if (wrappedKeyCommitted) File.Delete(wrappedKeyPath); } catch (Exception ex) { cleanupFailures.Add(ex); }
            try { if (artifactCommitted) File.Delete(encryptedPath); } catch (Exception ex) { cleanupFailures.Add(ex); }
            if (cleanupFailures.Count > 0)
                throw new IOException("Encrypted artifact setup failed and cleanup was incomplete.", new AggregateException([failure, .. cleanupFailures]));
            throw;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (protectedKey is not null) CryptographicOperations.ZeroMemory(protectedKey);
        }
    }

    [SupportedOSPlatform("windows")]
    public static void DecryptToDirectoryWithCurrentUserKey(string encryptedPath, string wrappedKeyPath, string backupId, string destinationDirectory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The local key-protection workflow requires Windows DPAPI.");
        var protectedKey = File.ReadAllBytes(wrappedKeyPath);
        byte[]? key = null;
        try
        {
            key = WindowsDpapiKeyProtector.Unprotect(protectedKey, backupId);
            using var input = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            DecryptToDirectory(input, key, destinationDirectory);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }

    // Low-level test/format primitive. It is intentionally not the proposed backup workflow.
    internal static void Encrypt(Stream destination, IReadOnlyList<SyntheticFile> files, ReadOnlySpan<byte> key, int chunkSize = 64 * 1024)
        => EncryptCore(destination, files, key, chunkSize);

    private static void EncryptCore(Stream destination, IReadOnlyList<SyntheticFile> files, ReadOnlySpan<byte> key, int chunkSize)
    {
        ValidateKey(key); if (destination is null) throw new ArgumentNullException(nameof(destination));
        if (chunkSize is <= 0 or > MaxChunkSize) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        var manifest = BuildManifest(files); var manifestBytes = CanonicalJson(manifest); if (manifestBytes.Length > MaxManifestBytes) throw new InvalidDataException("Manifest exceeds limit."); var chunkCount = ChunkCount(manifest.Files, chunkSize); if (chunkCount > MaxChunkCount) throw new InvalidDataException("Chunk count exceeds limit."); var id = RandomNumberGenerator.GetBytes(16); var header = CreateHeader(chunkSize, (ulong)manifestBytes.Length, chunkCount, id);
        using var aes = new AesGcm(key, TagSize); var mc = new byte[manifestBytes.Length]; var mt = new byte[TagSize]; aes.Encrypt(Nonce(id, 0), manifestBytes, mc, mt, header); destination.Write(header); WriteUInt32(destination, (uint)mc.Length); destination.Write(mc); destination.Write(mt);
        var digest = SHA256.HashData(manifestBytes); ulong record = 1;
        foreach (var file in files) for (var offset = 0; offset < file.Content.Length;) { var length = Math.Min(chunkSize, file.Content.Length - offset); WriteChunk(destination, aes, header, digest, record, file.Content.AsSpan(offset, length), chunkCount, id); offset += length; record++; }
        WriteEndRecord(destination, aes, header, digest, record, chunkCount, manifest.TotalLength, id);
    }

    // The compatibility result necessarily materializes small synthetic output. Real consumers must stream or extract to a directory.
    public static IReadOnlyList<SyntheticFile> Decrypt(Stream source, ReadOnlySpan<byte> key)
    {
        using var output = new MemoryStream(); var manifest = DecryptToStreamCore(source, output, key); output.Position = 0; var result = new List<SyntheticFile>(manifest.Files.Count);
        foreach (var entry in manifest.Files) { if (entry.Length > int.MaxValue) throw new InvalidDataException("File exceeds compatibility API limit."); var b = new byte[(int)entry.Length]; ReadFully(output, b); result.Add(new(entry.Path, b)); } return result;
    }
    // Deliberately internal: recovery callers must use DecryptToDirectory, which stages all
    // authenticated output and commits only after the terminal record and all file checks pass.
    internal static void DecryptToStream(Stream source, Stream destination, ReadOnlySpan<byte> key) => _ = DecryptToStreamCore(source, destination, key);

    private static Manifest DecryptToStreamCore(Stream source, Stream destination, ReadOnlySpan<byte> key)
    {
        ValidateKey(key); if (source is null || destination is null) throw new ArgumentNullException(); var header = ReadExactly(source, HeaderSize); ValidateHeader(header, out var chunkSize, out var manifestLength, out var chunkCount, out var id); var cipherLength = ReadUInt32(source); if (cipherLength != manifestLength || cipherLength > MaxManifestBytes) throw new InvalidDataException("Invalid manifest length.");
        var cipher = ReadExactly(source, (int)cipherLength); var tag = ReadExactly(source, TagSize); var plainManifest = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize); aes.Decrypt(Nonce(id, 0), cipher, tag, plainManifest, header); var manifest = ParseManifest(plainManifest, chunkCount, chunkSize); var digest = SHA256.HashData(plainManifest); ulong record = 1, remaining = manifest.TotalLength, consumed = 0; var fileIndex = 0; var fileOffset = 0UL; using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (record <= chunkCount) { var plain = ReadChunk(source, aes, header, digest, record, chunkCount, chunkSize, id); if ((ulong)plain.Length > remaining) throw new InvalidDataException("Chunk data exceeds manifest length."); var offset = 0; while (offset < plain.Length) { if (fileIndex >= manifest.Files.Count) throw new InvalidDataException("Unlisted package content."); var entry = manifest.Files[fileIndex]; var take = (int)Math.Min((ulong)(plain.Length - offset), entry.Length - fileOffset); fileHash.AppendData(plain, offset, take); destination.Write(plain, offset, take); offset += take; fileOffset += (ulong)take; consumed += (ulong)take; if (fileOffset == entry.Length) { if (!CryptographicOperations.FixedTimeEquals(fileHash.GetHashAndReset(), Convert.FromHexString(entry.Sha256))) throw new InvalidDataException("File hash mismatch."); fileIndex++; fileOffset = 0; } } remaining -= (ulong)plain.Length; record++; }
            while (fileIndex < manifest.Files.Count && manifest.Files[fileIndex].Length == 0) { if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Array.Empty<byte>()), Convert.FromHexString(manifest.Files[fileIndex].Sha256))) throw new InvalidDataException("File hash mismatch."); fileIndex++; }
            ReadEnd(source, aes, header, digest, record, chunkCount, manifest.TotalLength, id); if (remaining != 0 || consumed != manifest.TotalLength || fileIndex != manifest.Files.Count || source.Position != source.Length) throw new InvalidDataException("Invalid package boundary."); return manifest;
        }
        catch (CryptographicException) { throw new InvalidDataException("Package authentication failed."); }
        catch (OverflowException) { throw new InvalidDataException("Package length is invalid."); }
        catch (ArgumentException) { throw new InvalidDataException("Package metadata is invalid."); }
        catch (NullReferenceException) { throw new InvalidDataException("Manifest contains null metadata."); }
    }

    public static void DecryptToDirectory(Stream source, ReadOnlySpan<byte> key, string destinationDirectory)
    {
        ValidateDestination(destinationDirectory); var parent = Path.GetFullPath(destinationDirectory); var staging = parent + ".staging-" + Guid.NewGuid().ToString("N"); Exception? failure = null;
        try
        {
            RejectReparseChain(parent); Directory.CreateDirectory(staging); RejectReparseChain(staging);
            var stagedData = Path.Combine(staging, ".package-data.tmp"); Manifest manifest; using (var data = new FileStream(stagedData, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.WriteThrough)) { manifest = DecryptToStreamCore(source, data, key); data.Flush(true); data.Position = 0;
            foreach (var entry in manifest.Files) { var final = SafeCombine(staging, entry.Path); var directory = Path.GetDirectoryName(final)!; Directory.CreateDirectory(directory); RejectReparseChain(directory); var temp = final + ".tmp-" + Guid.NewGuid().ToString("N"); using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough)) { CopyExact(data, output, entry.Length); output.Flush(true); } File.Move(temp, final); } }
            File.Delete(stagedData);
            if (Directory.Exists(parent)) throw new InvalidDataException("Destination already exists; atomic commit refused."); Directory.Move(staging, parent);
        }
        catch (Exception ex)
        {
            failure = ex;
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception cleanup) { throw new InvalidDataException("Decryption failed and staging cleanup also failed; output was not committed.", new AggregateException(ex, cleanup)); }
            throw new InvalidDataException("Decryption failed; no output was committed.", failure);
        }
    }

    private static Manifest BuildManifest(IReadOnlyList<SyntheticFile> files)
    {
        if (files is null || files.Count > MaxFileCount) throw new InvalidDataException("File count exceeds limit."); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var entries = new List<ManifestEntry>(); ulong total = 0;
        foreach (var file in files) { var path = ValidatePath(file.RelativePath); if (!paths.Add(path)) throw new InvalidDataException("Path collision."); if ((ulong)file.Content.LongLength > MaxFileBytes) throw new InvalidDataException("File exceeds limit."); total = checked(total + (ulong)file.Content.LongLength); if (total > MaxTotalPlaintextBytes) throw new InvalidDataException("Package exceeds limit."); entries.Add(new(path, (ulong)file.Content.LongLength, Convert.ToHexString(SHA256.HashData(file.Content)))); } return new(entries, total);
    }
    private static Manifest ParseManifest(byte[] bytes, ulong chunkCount, int chunkSize)
    {
        if (bytes.Length == 0 || bytes.Length > MaxManifestBytes || bytes[0] == 0xEF) throw new InvalidDataException("Invalid manifest encoding.");
        try { using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 }); RejectDuplicates(doc.RootElement); var m = JsonSerializer.Deserialize<Manifest>(bytes) ?? throw new InvalidDataException("Empty manifest."); if (m.Files is null || !bytes.AsSpan().SequenceEqual(CanonicalJson(m))) throw new InvalidDataException("Noncanonical manifest."); if (m.Files.Count > MaxFileCount || m.TotalLength > MaxTotalPlaintextBytes || ChunkCount(m.Files, chunkSize) != chunkCount) throw new InvalidDataException("Manifest limits or lengths are inconsistent."); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); ulong total = 0; foreach (var f in m.Files) { if (f is null || f.Path is null || f.Sha256 is null) throw new InvalidDataException("Manifest contains null metadata."); var p = ValidatePath(f.Path); if (p != f.Path || !paths.Add(p) || f.Length > MaxFileBytes || f.Sha256.Length != 64 || !IsHex(f.Sha256)) throw new InvalidDataException("Invalid manifest entry."); total = checked(total + f.Length); } if (total != m.TotalLength) throw new InvalidDataException("Manifest total mismatch."); return m; }
        catch (InvalidDataException) { throw; } catch (JsonException) { throw new InvalidDataException("Malformed manifest."); } catch (OverflowException) { throw new InvalidDataException("Manifest length overflow."); } catch (ArgumentException) { throw new InvalidDataException("Malformed manifest metadata."); } catch (NullReferenceException) { throw new InvalidDataException("Manifest contains null metadata."); }
    }
    private static void RejectDuplicates(JsonElement e) { if (e.ValueKind == JsonValueKind.Object) { var s = new HashSet<string>(StringComparer.Ordinal); foreach (var p in e.EnumerateObject()) { if (!s.Add(p.Name)) throw new InvalidDataException("Duplicate manifest property."); RejectDuplicates(p.Value); } } else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) RejectDuplicates(x); }
    private static string ValidatePath(string path) { if (string.IsNullOrEmpty(path) || path.Normalize(NormalizationForm.FormC) != path || path.StartsWith('/') || path.StartsWith('\\') || path.Contains('\\') || Path.IsPathRooted(path) || path.Contains('\0') || path.Contains(':')) throw new InvalidDataException("Unsafe relative path."); var parts = path.Split('/'); if (parts.Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || IsReserved(p))) throw new InvalidDataException("Unsafe relative path."); return path; }
    private static bool IsReserved(string p) { var s = p.TrimEnd(' ', '.'); var dot = s.IndexOf('.'); if (dot >= 0) s = s[..dot]; return s.Equals("CON", StringComparison.OrdinalIgnoreCase) || s.Equals("PRN", StringComparison.OrdinalIgnoreCase) || s.Equals("AUX", StringComparison.OrdinalIgnoreCase) || s.Equals("NUL", StringComparison.OrdinalIgnoreCase) || (s.Length == 4 && (s.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || s.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && s[3] is >= '1' and <= '9'); }
    private static void ValidateDestination(string p)
    {
        if (string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p)) throw new ArgumentException("Unsafe extraction destination.", nameof(p));
        ValidateWindowsComponents(p, nameof(p));
        var full = Path.GetFullPath(p);
        if (!string.Equals(full, p, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Destination must already be normalized.", nameof(p));
        ValidateLocalVolume(full, nameof(p));
    }

    private static void ValidateDestinationFile(string destinationPath, string approvedStagingDirectory)
    {
        ValidateDestination(destinationPath);
        if (string.IsNullOrWhiteSpace(approvedStagingDirectory) || !Path.IsPathFullyQualified(approvedStagingDirectory)) throw new ArgumentException("An approved local staging directory is required.", nameof(approvedStagingDirectory));
        ValidateWindowsComponents(approvedStagingDirectory, nameof(approvedStagingDirectory));
        if (!string.Equals(Path.GetFullPath(approvedStagingDirectory), approvedStagingDirectory, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Approved staging directory must already be normalized.", nameof(approvedStagingDirectory));
        var staging = Path.GetFullPath(approvedStagingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destination = Path.GetFullPath(destinationPath);
        var parent = Path.GetDirectoryName(destination)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parent is null || !string.Equals(parent, staging, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Destination must be directly inside the approved staging directory.", nameof(destinationPath));
        ValidateLocalVolume(staging, nameof(approvedStagingDirectory));
        if (!Directory.Exists(staging) || (new DirectoryInfo(staging).Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Approved staging directory must already exist and cannot be a reparse point.");
        RejectReparseChain(staging);
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath)) throw new IOException("Destination already exists; overwrite is refused.");
    }

    private static void ValidateWindowsComponents(string path, string parameterName)
    {
        if (path.Contains('\0') || path.Contains('*') || path.Contains('?') || path.Contains('"') || path.Contains('<') || path.Contains('>') || path.Contains('|')) throw new ArgumentException("Destination contains invalid Windows path characters.", parameterName);
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) throw new ArgumentException("Destination has no Windows volume root.", parameterName);
        var rest = path[root.Length..];
        foreach (var component in rest.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (component.Contains(':') || component.Any(char.IsControl) || component.EndsWith(' ') || component.EndsWith('.') || IsReserved(component)) throw new ArgumentException("Destination contains an unsafe Windows path component.", parameterName);
        }
    }

    private static void ValidateLocalVolume(string path, string parameterName)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) throw new ArgumentException("UNC/shared destinations are not approved.", parameterName);
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) throw new ArgumentException("Destination has no local volume root.", parameterName);
        try { if (new DriveInfo(root).DriveType != DriveType.Fixed) throw new ArgumentException("Destination must be on a fixed local volume.", parameterName); }
        catch (ArgumentException) { throw; }
        catch (Exception ex) { throw new ArgumentException("Destination volume could not be approved.", parameterName, ex); }
    }
    private static string SafeCombine(string root, string relative) { var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))); if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe extraction path."); return full; }
    private static void RejectReparseChain(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse-point extraction path is not allowed.");
            current = current.Parent;
        }
    }
    private static bool IsHex(string s) { try { _ = Convert.FromHexString(s); return true; } catch (FormatException) { return false; } }
    private static ulong ChunkCount(List<ManifestEntry> files, int size) { ulong count = 0; foreach (var file in files) if (file.Length != 0) count = checked(count + (file.Length + (ulong)size - 1) / (ulong)size); return count; }
    private static byte[] CreateHeader(int size, ulong manifest, ulong count, byte[] id) { var h = new byte[HeaderSize]; Magic.CopyTo(h); h[4] = Version; BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(6), (uint)size); BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(10), manifest); BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(18), count); id.CopyTo(h, 26); id.AsSpan(0, NonceSize).CopyTo(h.AsSpan(42)); return h; }
    private static void ValidateHeader(byte[] h, out int size, out ulong manifest, out ulong count, out byte[] id) { if (!h.AsSpan(0, 4).SequenceEqual(Magic) || h[4] != Version || h[5] != 0 || !h.AsSpan(42, NonceSize).SequenceEqual(h.AsSpan(26, NonceSize))) throw new InvalidDataException("Unsupported package header."); size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(6))); manifest = BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(10)); count = BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(18)); id = h.AsSpan(26, 16).ToArray(); if (size <= 0 || size > MaxChunkSize || manifest > MaxManifestBytes || count > MaxChunkCount) throw new InvalidDataException("Package limits exceeded."); }
    private static byte[] Nonce(byte[] id, ulong record) { if (record > ulong.MaxValue - 1) throw new InvalidDataException("Nonce counter exhausted."); var n = new byte[NonceSize]; id.AsSpan(0, 4).CopyTo(n); BinaryPrimitives.WriteUInt64LittleEndian(n.AsSpan(4), record); return n; }
    private static void WriteChunk(Stream d, AesGcm aes, byte[] h, byte[] digest, ulong record, ReadOnlySpan<byte> plain, ulong count, byte[] id) { var c = new byte[plain.Length]; var tag = new byte[TagSize]; aes.Encrypt(Nonce(id, record), plain, c, tag, A(h, digest, record, (uint)plain.Length, count)); WriteUInt64(d, record); WriteUInt32(d, (uint)plain.Length); d.Write(c); d.Write(tag); }
    private static byte[] ReadChunk(Stream s, AesGcm aes, byte[] h, byte[] digest, ulong record, ulong count, int size, byte[] id) { var index = ReadUInt64(s); var length = ReadUInt32(s); if (index != record || length > size || length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid chunk metadata."); var c = ReadExactly(s, (int)length); var tag = ReadExactly(s, TagSize); var p = new byte[length]; aes.Decrypt(Nonce(id, record), c, tag, p, A(h, digest, record, length, count)); return p; }
    private static void WriteEndRecord(Stream d, AesGcm aes, byte[] h, byte[] digest, ulong record, ulong count, ulong total, byte[] id) { d.Write(EndMagic); WriteUInt64(d, count); WriteUInt64(d, total); var tag = new byte[TagSize]; aes.Encrypt(Nonce(id, record), ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, A(EndMagic, h, digest, record, count, total)); d.Write(tag); }
    private static void ReadEnd(Stream s, AesGcm aes, byte[] h, byte[] digest, ulong record, ulong count, ulong total, byte[] id) { var magic = ReadExactly(s, 4); var actualCount = ReadUInt64(s); var actualTotal = ReadUInt64(s); if (!magic.AsSpan().SequenceEqual(EndMagic) || actualCount != count || actualTotal != total) throw new InvalidDataException("Invalid package end record."); var tag = ReadExactly(s, TagSize); aes.Decrypt(Nonce(id, record), ReadOnlySpan<byte>.Empty, tag, Span<byte>.Empty, A(EndMagic, h, digest, record, count, total)); }
    private static byte[] A(params object[] values) { using var m = new MemoryStream(); foreach (var v in values) { if (v is byte[] b) m.Write(b); else if (v is ulong u) WriteUInt64(m, u); else if (v is uint n) WriteUInt32(m, n); } return m.ToArray(); }
    private static byte[] CanonicalJson(Manifest m) => JsonSerializer.SerializeToUtf8Bytes(m, new JsonSerializerOptions { WriteIndented = false });
    private static byte[] ReadExactly(Stream s, int count) { if (count < 0 || count > 4 * 1024 * 1024) throw new InvalidDataException("Invalid allocation length."); var b = new byte[count]; ReadFully(s, b); return b; }
    private static void ReadFully(Stream s, byte[] b) { var o = 0; while (o < b.Length) { var n = s.Read(b, o, b.Length - o); if (n <= 0) throw new InvalidDataException("Truncated package."); o += n; } }
    private static void CopyExact(Stream s, Stream d, ulong length) { var b = new byte[64 * 1024]; while (length > 0) { var n = (int)Math.Min((ulong)b.Length, length); var part = new byte[n]; ReadFully(s, part); d.Write(part); length -= (ulong)n; } }
    private static ulong ReadUInt64(Stream s) { var b = ReadExactly(s, 8); return BinaryPrimitives.ReadUInt64LittleEndian(b); } private static uint ReadUInt32(Stream s) { var b = ReadExactly(s, 4); return BinaryPrimitives.ReadUInt32LittleEndian(b); }
    private static void WriteUInt64(Stream s, ulong x) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, x); s.Write(b); } private static void WriteUInt32(Stream s, uint x) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, x); s.Write(b); }
    private static void ValidateKey(ReadOnlySpan<byte> key) { if (key.Length != KeySize) throw new ArgumentException("AES-256 key required.", nameof(key)); }
    private sealed record Manifest([property: JsonPropertyName("Files")] List<ManifestEntry> Files, [property: JsonPropertyName("TotalLength")] ulong TotalLength);
    private sealed record ManifestEntry([property: JsonPropertyName("Path")] string Path, [property: JsonPropertyName("Length")] ulong Length, [property: JsonPropertyName("Sha256")] string Sha256);
}

public sealed record ProtectedBackupArtifact(string BackupId, string ArtifactPath, string ProtectedKeyPath, string Sha256,
    string EncryptionAlgorithm, string KeyProtection);

internal static class PrototypeTests
{
    public static void RunAll()
    {
        RunStreamingArchiveTests();
        if (OperatingSystem.IsWindows()) RunWindowsDpapiTests();
        var key = RandomNumberGenerator.GetBytes(32); var files = new[] { new SyntheticFile("exports/alpha.txt", Encoding.UTF8.GetBytes("alpha")), new SyntheticFile("exports/beta.bin", Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray()) }; using var p = new MemoryStream(); BackupPackage.Encrypt(p, files, key, 4096); var bytes = p.ToArray(); var restored = BackupPackage.Decrypt(new MemoryStream(bytes), key); Assert(restored.Count == 2 && restored[1].Content.SequenceEqual(files[1].Content), "round trip");
        using var freshPackage1 = new MemoryStream(); var freshKey1 = BackupPackage.EncryptWithFreshKey(freshPackage1, files, 4096); using var freshPackage2 = new MemoryStream(); var freshKey2 = BackupPackage.EncryptWithFreshKey(freshPackage2, files, 4096); Assert(!freshKey1.SequenceEqual(freshKey2), "fresh-key workflow generates distinct keys"); Assert(BackupPackage.Decrypt(new MemoryStream(freshPackage1.ToArray()), freshKey1).Count == 2, "fresh-key workflow round trip");
        Expect(() => BackupPackage.Decrypt(new MemoryStream(bytes), RandomNumberGenerator.GetBytes(32))); Expect(() => BackupPackage.Decrypt(new MemoryStream(bytes[..^1]), key)); Expect(() => BackupPackage.Decrypt(new MemoryStream(bytes.Concat(new byte[] { 1 }).ToArray()), key)); var tampered = bytes.ToArray(); tampered[^1] ^= 1; Expect(() => BackupPackage.Decrypt(new MemoryStream(tampered), key));
        foreach (var path in new[] { "../x", "CON.txt", "dir/file. ", "dir. /file", "dir./file", "dir /file", "a/../b", "a//b", "C:x", "e\u0301.txt", "a/e\u0301.txt", "a/CON.txt", "a\\b" }) Expect(() => BackupPackage.Encrypt(new MemoryStream(), new[] { new SyntheticFile(path, new byte[] { 1 }) }, key)); Expect(() => BackupPackage.Encrypt(new MemoryStream(), new[] { new SyntheticFile("Dir/File", new byte[] { 1 }), new SyntheticFile("dir/file", new byte[] { 2 }) }, key)); Expect(() => BackupPackage.Encrypt(new MemoryStream(), new[] { new SyntheticFile("a/é.txt", new byte[] { 1 }), new SyntheticFile("a/é.txt", new byte[] { 2 }) }, key));
        Expect(() => BackupPackage.DecryptToDirectory(new MemoryStream(bytes), key, Path.Combine(Path.GetTempPath(), "unsafe. ", "restore")));
        var sandbox = Path.Combine(Path.GetTempPath(), "backup-encryption-prototype-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sandbox);
        try
        {
            var output = Path.Combine(sandbox, "package.hreb"); var returnedKey = BackupPackage.EncryptToFile(output, files, sandbox, 4096); Assert(File.Exists(output), "atomic encrypted output exists"); using (var input = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert(BackupPackage.Decrypt(input, returnedKey).Count == 2, "atomic encrypted output verifies");
            ExpectAny(() => BackupPackage.EncryptToFile(output, files, sandbox, 4096)); Assert(Directory.GetFiles(sandbox, ".backup-encryption-*.tmp").Length == 0, "staging cleanup after existing destination refusal");
            foreach (var unsafeName in new[] { "CON.hreb", "bad. ", "bad:", "bad?.hreb", "folder\\package.hreb" }) ExpectAny(() => BackupPackage.EncryptToFile(Path.Combine(sandbox, unsafeName), files, sandbox));
            using var interrupted = new ThrowingWriteStream(32); ExpectAny(() => BackupPackage.Encrypt(interrupted, files, key, 4096)); Assert(interrupted.WasInterrupted, "interrupted encryption simulated");
            var link = Path.Combine(sandbox, "reparse"); var reparseCreated = false;
            try { Directory.CreateSymbolicLink(link, sandbox); reparseCreated = true; } catch (UnauthorizedAccessException) { } catch (IOException) { }
            if (reparseCreated) ExpectAny(() => BackupPackage.EncryptToFile(Path.Combine(link, "linked.hreb"), files, link)); else Console.WriteLine("Tests: reparse-point creation unavailable; rejection path not exercised.");
        }
        finally { Directory.Delete(sandbox, true); }
        Expect(() => BackupPackage.Encrypt(new MemoryStream(), files, key, 0)); Expect(() => BackupPackage.Encrypt(new MemoryStream(), new[] { new SyntheticFile(new string('x', BackupPackage.MaxManifestBytes), new byte[] { 1 }) }, key)); Expect(() => BackupPackage.Encrypt(new MemoryStream(), Enumerable.Range(0, BackupPackage.MaxFileCount + 1).Select(i => new SyntheticFile($"f{i}", Array.Empty<byte>())).ToArray(), key));
        using var empty = new MemoryStream(); BackupPackage.Encrypt(empty, Array.Empty<SyntheticFile>(), key); Assert(BackupPackage.Decrypt(new MemoryStream(empty.ToArray()), key).Count == 0, "zero-length package"); using var zeroFile = new MemoryStream(); BackupPackage.Encrypt(zeroFile, new[] { new SyntheticFile("empty.txt", Array.Empty<byte>()) }, key); Assert(BackupPackage.Decrypt(new MemoryStream(zeroFile.ToArray()), key)[0].Content.Length == 0, "empty file"); Console.WriteLine("Tests: streaming round-trip, authentication, truncation, trailing-data, atomic output, interrupted writes, destination policy, reparse rejection, cleanup, empty-file and zero-package behavior, bounded metadata, resource-limit rejection");
    }

    private static void RunStreamingArchiveTests()
    {
        const string backupId = "synthetic-stream-01";
        var key = RandomNumberGenerator.GetBytes(32);
        var root = Path.Combine(Path.GetTempPath(), "hrms-stream-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifact = Path.Combine(root, "synthetic.hrb");
        var length = 32L * 1024 * 1024;
        try
        {
            using (var input = new PatternStream(length))
            using (var output = new FileStream(artifact, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
                StreamingBackupArchive.EncryptWithKey(input, output, backupId, key, 64 * 1024);

            using (var source = new FileStream(artifact, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            {
                source.Position = 0;
                Assert(StreamingBackupArchive.Verify(source, key, backupId).BackupId == backupId, "large streaming artifact verifies");
            }
        }
        finally { Directory.Delete(root, recursive: true); }

        // Small deterministic fixture covers format attacks while keeping test diagnostics compact.
        using var plain = new MemoryStream(Enumerable.Range(0, 12_000).Select(i => (byte)(i % 239)).ToArray());
        using var encoded = new MemoryStream();
        StreamingBackupArchive.EncryptWithKey(plain, encoded, backupId, key, 4096);
        var original = encoded.ToArray();
        using (var verify = new MemoryStream(original)) Assert(StreamingBackupArchive.Verify(verify, key, backupId).BackupId == backupId, "streaming archive verifies");
        using (var verifyAndDecrypt = new MemoryStream(original))
        using (var restored = new MemoryStream())
        {
            StreamingBackupArchive.VerifyThenDecrypt(verifyAndDecrypt, restored, key, backupId);
            Assert(restored.ToArray().SequenceEqual(Enumerable.Range(0, 12_000).Select(i => (byte)(i % 239))), "verify-before-decrypt round trip");
        }
        ExpectAny(() => StreamingBackupArchive.Verify(new MemoryStream(original), key, "wrong-id"));
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var wrongSource = new MemoryStream(original);
            using var noPlaintext = new HashingNullStream();
            ExpectCrypto(() => StreamingBackupArchive.VerifyThenDecrypt(wrongSource, noPlaintext, wrongKey, backupId));
        }
        finally { CryptographicOperations.ZeroMemory(wrongKey); }
        ExpectAny(() => StreamingBackupArchive.Verify(new MemoryStream(original[..^5]), key, backupId));
        using (var truncatedSource = new MemoryStream(original[..^5]))
        using (var counting = new CountingStream())
        {
            ExpectAny(() => StreamingBackupArchive.VerifyThenDecrypt(truncatedSource, counting, key, backupId));
            Assert(counting.BytesWritten == 0, "truncation rejected before plaintext emission");
        }
        var metadataTamper = original.ToArray();
        var headerLength = BinaryPrimitives.ReadUInt32BigEndian(metadataTamper.AsSpan(8, 4));
        var purposeIndex = Array.IndexOf(metadataTamper, (byte)'H', 12, checked((int)headerLength));
        Assert(purposeIndex >= 0, "metadata test marker exists"); metadataTamper[purposeIndex] ^= 1;
        ExpectAny(() => StreamingBackupArchive.Verify(new MemoryStream(metadataTamper), key, backupId));
        var chunkTamper = original.ToArray();
        var firstChunk = 12 + checked((int)headerLength) + 16 + 12;
        chunkTamper[firstChunk + 9] ^= 0x40;
        ExpectAny(() => StreamingBackupArchive.Verify(new MemoryStream(chunkTamper), key, backupId));
        var reordered = ReorderFirstTwoChunks(original, checked((int)headerLength));
        ExpectAny(() => StreamingBackupArchive.Verify(new MemoryStream(reordered), key, backupId));
        Console.WriteLine("Tests: streamed 32 MiB input with bounded buffers; round-trip, header authentication, ciphertext tamper, truncation, wrong backup ID, reordered chunks, and verify-before-decrypt.");
        CryptographicOperations.ZeroMemory(key);
    }

    private static byte[] ReorderFirstTwoChunks(byte[] archive, int headerLength)
    {
        var first = 12 + headerLength + 16;
        var firstLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(archive.AsSpan(first + 8, 4)));
        var firstRecordLength = 12 + firstLength + 16;
        var second = first + firstRecordLength;
        var secondLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(archive.AsSpan(second + 8, 4)));
        var secondRecordLength = 12 + secondLength + 16;
        var result = archive[..first].Concat(archive.AsSpan(second, secondRecordLength).ToArray())
            .Concat(archive.AsSpan(first, firstRecordLength).ToArray())
            .Concat(archive.AsSpan(second + secondRecordLength).ToArray()).ToArray();
        return result;
    }

    private sealed class PatternStream(long length) : Stream
    {
        private long _position;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _position);
            for (var i = 0; i < n; i++) buffer[offset + i] = (byte)((_position + i) % 251);
            _position += n;
            return n;
        }
        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, length - _position);
            for (var i = 0; i < n; i++) buffer[i] = (byte)((_position + i) % 251);
            _position += n;
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class HashingNullStream : Stream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new Exception("Plaintext was released before full authentication.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new Exception("Plaintext was released before full authentication.");
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => 0; public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
    private sealed class CountingStream : Stream
    {
        public long BytesWritten { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;
        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => BytesWritten; public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }

    [SupportedOSPlatform("windows")]
    private static void RunWindowsDpapiTests()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapped = WindowsDpapiKeyProtector.Protect(key, "synthetic-backup-id");
            var recovered = WindowsDpapiKeyProtector.Unprotect(wrapped, "synthetic-backup-id");
            try { Assert(CryptographicOperations.FixedTimeEquals(key, recovered), "Windows DPAPI current-user key-wrap round trip"); }
            finally { CryptographicOperations.ZeroMemory(recovered); }
            ExpectCrypto(() => WindowsDpapiKeyProtector.Unprotect(wrapped, "different-backup-id"));

            var root = Path.Combine(Path.GetTempPath(), "hrms-dpapi-package-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var encryptedPath = Path.Combine(root, "synthetic.hreb");
            var restorePath = Path.Combine(root, "isolated-restore");
            try
            {
                var syntheticFiles = new[] { new SyntheticFile("synthetic.txt", "synthetic-only"u8.ToArray()) };
                var artifact = BackupPackage.EncryptToFileWithCurrentUserKey(encryptedPath, syntheticFiles, root, "synthetic-package-1", 4096);
                Assert(artifact.Sha256.Length == 64 && File.Exists(artifact.ProtectedKeyPath), "DPAPI-wrapped backup key metadata");
                BackupPackage.DecryptToDirectoryWithCurrentUserKey(artifact.ArtifactPath, artifact.ProtectedKeyPath, artifact.BackupId, restorePath);
                Assert(File.ReadAllText(Path.Combine(restorePath, "synthetic.txt")) == "synthetic-only", "DPAPI-wrapped package restore");
                ExpectCrypto(() => BackupPackage.DecryptToDirectoryWithCurrentUserKey(artifact.ArtifactPath, artifact.ProtectedKeyPath, "wrong-package", Path.Combine(root, "wrong-restore")));
            }
            finally { Directory.Delete(root, recursive: true); }

            RunSyntheticPublisherAndRestoreTest();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    [SupportedOSPlatform("windows")]
    private static void RunSyntheticPublisherAndRestoreTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "hrms-stream-publish-test-" + Guid.NewGuid().ToString("N"));
        StreamingArtifactPublisher.InitializeNewProtectedRoot(root);
        try
        {
            var sourceUuid = "synthetic-source-uuid";
            var targetIdentity = new RestoreTargetIdentity("127.0.0.1", 43306, "synthetic-restore-uuid", "hrms_restore_synthetic", true, true, true, "VerifyCA");
            var sourceSnapshot = new DatabaseSnapshot(3, new string('A', 64), ["migration-1", "migration-2"],
                new ImportAggregate("b89d04aa-1419-4c85-a7e3-48ab1d92d111", "Processing", 10_000, 0, 1, 2_844, 7_155, "expired"), true);
            var target = new SyntheticRestoreTarget(targetIdentity, sourceSnapshot);
            using var dump = new MemoryStream("synthetic SQL stream only"u8.ToArray());
            var published = StreamingArtifactPublisher.Publish(dump, root, "synthetic-backup-restore-01",
                new StreamingBackupArchive.ArchiveScope(sourceUuid, "WEB1", "hrms_web1", "127.0.0.1", 3306, "hrms_catalog", "MySql", "VerifyCA"), sourceSnapshot, () => 0, () => { }, 4096);
            Assert(File.Exists(published.ArtifactPath) && File.Exists(published.ProtectedKeyPath) && published.Sha256.Length == 64,
                "atomic encrypted artifact and wrapped-key publication");
            var result = IsolatedRestoreVerifier.VerifyAndRestore(published.ArtifactPath, published.ProtectedKeyPath,
                published.BackupId, published.Sha256, new RestoreSourceIdentity(sourceUuid, "WEB1", "hrms_web1", "127.0.0.1", 3306, "hrms_catalog", "MySql", "VerifyCA"),
                sourceSnapshot, targetIdentity, target);
            Assert(result.Target.ServerUuid != sourceUuid && target.RestoredInput == "synthetic SQL stream only", "isolated restore identity and authenticated stream");

            var wrongUserTarget = new SyntheticRestoreTarget(targetIdentity, sourceSnapshot);
            ExpectCrypto(() => IsolatedRestoreVerifier.VerifyAndRestore(published.ArtifactPath, published.ProtectedKeyPath,
                published.BackupId, published.Sha256, new RestoreSourceIdentity(sourceUuid, "WEB1", "hrms_web1", "127.0.0.1", 3306, "hrms_catalog", "MySql", "VerifyCA"),
                sourceSnapshot, targetIdentity, wrongUserTarget, new WrongUserDpapiUnwrapper()));
            Assert(!wrongUserTarget.ImportOpened, "DPAPI wrong-user failure occurs before restore starts");

            var failedRestore = new SyntheticRestoreTarget(targetIdentity, sourceSnapshot with { TableCount = 4 });
            ExpectAny(() => IsolatedRestoreVerifier.VerifyAndRestore(published.ArtifactPath, published.ProtectedKeyPath,
                published.BackupId, published.Sha256, new RestoreSourceIdentity(sourceUuid, "WEB1", "hrms_web1", "127.0.0.1", 3306, "hrms_catalog", "MySql", "VerifyCA"),
                sourceSnapshot, targetIdentity, failedRestore));
            Assert(failedRestore.WasAborted, "mismatched isolated restore is aborted");

            var badTarget = new SyntheticRestoreTarget(targetIdentity with { ServerUuid = sourceUuid }, sourceSnapshot);
            ExpectAny(() => IsolatedRestoreVerifier.VerifyAndRestore(published.ArtifactPath, published.ProtectedKeyPath,
                published.BackupId, published.Sha256, new RestoreSourceIdentity(sourceUuid, "WEB1", "hrms_web1", "127.0.0.1", 3306, "hrms_catalog", "MySql", "VerifyCA"),
                sourceSnapshot, targetIdentity with { ServerUuid = sourceUuid }, badTarget));

            using var failedProducer = new MemoryStream("partial"u8.ToArray());
            ExpectAny(() => StreamingArtifactPublisher.Publish(failedProducer, root, "synthetic-failed-producer",
                new StreamingBackupArchive.ArchiveScope(sourceUuid, "WEB1", "hrms_web1", "127.0.0.1", 3306, "hrms_catalog", "MySql", "VerifyCA"), sourceSnapshot, () => 17, () => { }, 4096));
            Assert(!Directory.Exists(Path.Combine(root, "synthetic-failed-producer")) &&
                !Directory.EnumerateDirectories(root, ".staging-*", SearchOption.TopDirectoryOnly).Any(), "failed producer staging cleanup");
            Console.WriteLine("Tests: DPAPI-wrapped atomic publish, synthetic isolated restore, wrong-target rejection, and interrupted producer cleanup.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class SyntheticRestoreTarget(RestoreTargetIdentity identity, DatabaseSnapshot snapshot) : IIsolatedRestoreTarget
    {
        private readonly MemoryStream _input = new();
        public bool WasAborted { get; private set; }
        public string RestoredInput => Encoding.UTF8.GetString(_input.ToArray());
        public RestoreTargetIdentity InspectIdentity() => identity;
        public bool ImportOpened { get; private set; }
        public Stream OpenImportStream() { ImportOpened = true; return _input; }
        public void CompleteImport() { }
        public void AbortImport() => WasAborted = true;
        public DatabaseSnapshot InspectRestoredSnapshot() => snapshot;
    }
    private sealed class WrongUserDpapiUnwrapper : IBackupDataKeyUnwrapper
    {
        public byte[] Unprotect(ReadOnlySpan<byte> protectedKey, string backupId)
            => throw new CryptographicException("Synthetic DPAPI user-profile mismatch.");
    }
    private static void Expect(Action a) { try { a(); throw new Exception("Expected validation failure."); } catch (InvalidDataException) { } catch (ArgumentException) { } }
    private static void ExpectCrypto(Action a) { try { a(); throw new Exception("Expected cryptographic failure."); } catch (CryptographicException) { } }
    private static void ExpectAny(Action a) { try { a(); throw new Exception("Expected failure."); } catch (InvalidDataException) { } catch (InvalidOperationException) { } catch (ArgumentException) { } catch (IOException) { } catch (CryptographicException) { } }
    private static void Assert(bool x, string n) { if (!x) throw new Exception($"Assertion failed: {n}"); }
    private sealed class ThrowingWriteStream(int limit) : MemoryStream
    {
        public bool WasInterrupted { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) { if (Position + count > limit) { WasInterrupted = true; throw new IOException("Synthetic disk/write failure."); } base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { if (Position + buffer.Length > limit) { WasInterrupted = true; throw new IOException("Synthetic disk/write failure."); } base.Write(buffer); }
    }
}
