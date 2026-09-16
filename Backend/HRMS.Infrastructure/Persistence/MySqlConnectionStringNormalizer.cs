using System.Data.Common;
using MySql.Data.MySqlClient;

namespace HRMS.Infrastructure.Persistence;

internal static class MySqlConnectionStringNormalizer
{
    private static readonly string[] TlsMaterialKeys =
    [
        "CertificateFile", "CertificatePassword", "SslCa", "SslCert", "SslKey",
        "TlsVersion", "CertificateThumbprint"
    ];

    /// <summary>
    /// MySql.Data on Windows can fail local development TLS negotiation with
    /// "No credentials are available in the security package". Development has
    /// no requirement to encrypt loopback traffic, so make the provider's
    /// explicit no-TLS value authoritative and remove conflicting TLS material.
    /// Production strings are returned unchanged.
    /// </summary>
    internal static string ForRuntime(string connectionString, bool isDevelopment)
    {
        if (!isDevelopment)
            return connectionString;

        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            SslMode = MySqlSslMode.Disabled,
            AllowPublicKeyRetrieval = true
        };

        foreach (var key in TlsMaterialKeys)
            builder.Remove(key);

        // Keep the provider-specific spelling explicit in the serialized value.
        builder["SslMode"] = "Disabled";
        var explicitOptions = new DbConnectionStringBuilder
        {
            ConnectionString = builder.ConnectionString
        };
        explicitOptions["SslMode"] = "Disabled";
        return explicitOptions.ConnectionString;
    }
}
