using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HRMS.Infrastructure.Storage;

public enum EmployeeImportStorageProvider { FileSystem, S3 }
public enum EmployeeImportStorageCredentialMode { DefaultChain, Static }

public sealed record EmployeeImportStorageOptions(
    EmployeeImportStorageProvider Provider,
    int RetentionDays,
    string? RootPath,
    string? Bucket,
    Uri? Endpoint,
    string? Region,
    bool UseTls,
    bool AllowInsecureDevelopmentEndpoint,
    EmployeeImportStorageCredentialMode CredentialMode,
    string? AccessKey,
    string? SecretKey,
    string? SessionToken)
{
    private static readonly Regex BucketPattern = new("\\A[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex KeyIdPattern = new("\\A[A-Za-z0-9._-]{1,64}\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static EmployeeImportStorageOptions LoadAndValidate(IConfiguration configuration, IHostEnvironment? environment)
    {
        var section = configuration.GetSection("EmployeeImportStorage");
        var providerName = section["Provider"];
        if (!Enum.TryParse<EmployeeImportStorageProvider>(providerName, ignoreCase: true, out var provider))
            throw new InvalidOperationException("EmployeeImportStorage:Provider must be FileSystem or S3.");
        var retention = section.GetValue<int?>("RetentionDays");
        if (retention is null or < 1 or > 3650)
            throw new InvalidOperationException("EmployeeImportStorage:RetentionDays must be between 1 and 3650.");

        ValidateEncryptionKeyRing(configuration);
        var root = section["RootPath"];
        var bucket = section["S3:Bucket"];
        var endpointText = section["S3:Endpoint"];
        var region = section["S3:Region"];
        var useTls = section.GetValue<bool?>("S3:UseTls") ?? true;
        var allowInsecureDevelopment = section.GetValue<bool>("S3:AllowInsecureDevelopmentEndpoint");
        var credentialModeText = section["S3:Credentials:Mode"] ?? "DefaultChain";
        if (!Enum.TryParse<EmployeeImportStorageCredentialMode>(credentialModeText, true, out var credentialMode))
            throw new InvalidOperationException("EmployeeImportStorage:S3:Credentials:Mode must be DefaultChain or Static.");
        var accessKey = section["S3:Credentials:AccessKey"];
        var secretKey = section["S3:Credentials:SecretKey"];
        var sessionToken = section["S3:Credentials:SessionToken"];
        if (credentialMode == EmployeeImportStorageCredentialMode.Static &&
            (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey)))
            throw new InvalidOperationException("S3 static credentials require externally supplied AccessKey and SecretKey values.");
        if (credentialMode == EmployeeImportStorageCredentialMode.DefaultChain &&
            (!string.IsNullOrWhiteSpace(accessKey) || !string.IsNullOrWhiteSpace(secretKey) || !string.IsNullOrWhiteSpace(sessionToken)))
            throw new InvalidOperationException("Do not set static credential values when S3 credential mode is DefaultChain.");

        Uri? endpoint = null;
        if (provider == EmployeeImportStorageProvider.FileSystem)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
                throw new InvalidOperationException("EmployeeImportStorage:RootPath must be configured as an absolute private path.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(bucket) || !BucketPattern.IsMatch(bucket) || bucket.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("EmployeeImportStorage:S3:Bucket must be a valid private bucket name.");
            if (!Uri.TryCreate(endpointText, UriKind.Absolute, out endpoint) || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
                throw new InvalidOperationException("EmployeeImportStorage:S3:Endpoint must be an absolute HTTP(S) endpoint without embedded credentials or query parameters.");
            if (string.IsNullOrWhiteSpace(region))
                throw new InvalidOperationException("EmployeeImportStorage:S3:Region must be configured for request signing.");
            var isHttps = endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            if (useTls != isHttps)
                throw new InvalidOperationException("EmployeeImportStorage:S3:UseTls must agree with the endpoint scheme.");
            if (!useTls && (!(environment?.IsDevelopment() ?? false) || !allowInsecureDevelopment))
                throw new InvalidOperationException("S3 storage requires TLS outside Development; insecure endpoints require explicit Development-only opt-in.");
        }

        return new(provider, retention.Value, root, bucket, endpoint, region, useTls, allowInsecureDevelopment,
            credentialMode, accessKey, secretKey, sessionToken);
    }

    private static void ValidateEncryptionKeyRing(IConfiguration configuration)
    {
        var active = configuration["EmployeeImportStorage:Encryption:ActiveKeyId"];
        if (string.IsNullOrWhiteSpace(active) || !KeyIdPattern.IsMatch(active))
            throw new InvalidOperationException("EmployeeImportStorage:Encryption:ActiveKeyId must be configured.");
        var foundActive = false;
        foreach (var child in configuration.GetSection("EmployeeImportStorage:Encryption:Keys").GetChildren())
        {
            if (!KeyIdPattern.IsMatch(child.Key) || string.IsNullOrWhiteSpace(child.Value))
                throw new InvalidOperationException("Employee import encryption key ring configuration is invalid.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(child.Value); }
            catch (FormatException) { throw new InvalidOperationException("Employee import encryption keys must be Base64-encoded 32-byte keys."); }
            try
            {
                if (bytes.Length != 32) throw new InvalidOperationException("Employee import encryption keys must be Base64-encoded 32-byte keys.");
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            foundActive |= string.Equals(child.Key, active, StringComparison.Ordinal);
        }
        if (!foundActive) throw new InvalidOperationException("The active employee import encryption key is missing from the configured key ring.");
    }
}
