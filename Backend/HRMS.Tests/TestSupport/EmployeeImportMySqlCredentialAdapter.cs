using System.Security;
using System.Diagnostics;
using MySql.Data.MySqlClient;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Test-only credential boundary. MySql.Data has no mysql_config_editor login-path support.
/// Provider connections therefore use a non-secret descriptor plus interactive passwords.
/// The login path is reserved for the separate mysql CLI preflight command.
/// </summary>
internal static class EmployeeImportMySqlCredentialAdapter
{
    private static readonly AsyncLocal<PromptSession?> Current = new();

    public static IDisposable BeginPromptSession(EmployeeImportMySqlAcceptanceConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.AdminLoginPath)) return NoopScope.Instance;
        var previous = Current.Value;
        var session = new PromptSession();
        Current.Value = session;
        return new Scope(previous, session);
    }

    public static string Connection(EmployeeImportMySqlAcceptanceConfiguration configuration, string database)
    {
        var raw = Current.Value?.AdministratorConnection(configuration, database)
            ?? configuration.ConnectionString
            ?? throw new InvalidOperationException("The F1 administrator credential source is not configured.");
        return WithDatabase(raw, database);
    }

    public static string ApplicationConnection(EmployeeImportMySqlAcceptanceConfiguration configuration, string database)
    {
        var raw = Current.Value?.ApplicationConnection(configuration, database) ?? configuration.ApplicationConnectionString;
        return WithDatabase(raw, database);
    }

    private static string WithDatabase(string raw, string database) =>
        new MySqlConnectionStringBuilder(raw) { Database = database, Pooling = false }.ConnectionString;

    internal static IReadOnlyDictionary<string, string> ParseOutput(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (key is "user" or "password" or "host" or "port") values[key] = value;
        }
        if (values.TryGetValue("password", out var password) && IsMasked(password))
            throw new InvalidOperationException("mysql_config_editor returned a masked password; it cannot authenticate the .NET provider.");
        if (!values.ContainsKey("user") || !values.ContainsKey("password") || !values.ContainsKey("host") || !values.ContainsKey("port"))
            throw new InvalidOperationException("mysql_config_editor did not return a complete usable F1 login path.");
        return values;
    }

    internal static bool IsMasked(string value) => value.Trim() is "*****" or "******" or "<hidden>" or "<redacted>";

    internal static string BuildConnectionString(IReadOnlyDictionary<string, string> values, int approvedPort)
    {
        if (!values.TryGetValue("host", out var host) || !values.TryGetValue("port", out var portText) ||
            !values.TryGetValue("user", out var user) || !values.TryGetValue("password", out var password) ||
            IsMasked(password) || !string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
            !uint.TryParse(portText, out var port) || port != approvedPort || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("The F1 login path does not provide a usable approved loopback credential.");
        return new MySqlConnectionStringBuilder { Server = host, Port = port, UserID = user, Password = password, Database = "mysql", Pooling = false }.ConnectionString;
    }

    /// <summary>CLI preflight only; this never supplies a provider password.</summary>
    internal static void VerifyLoginPathWithCli(EmployeeImportMySqlAcceptanceConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.AdminLoginPath) || string.IsNullOrWhiteSpace(configuration.MySqlConfigEditorPath))
            throw new InvalidOperationException("The F1 login-path CLI verification is not configured.");
        using var process = new Process { StartInfo = new ProcessStartInfo
        {
            FileName = configuration.MySqlConfigEditorPath, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        }};
        process.StartInfo.ArgumentList.Add("print");
        process.StartInfo.ArgumentList.Add($"--login-path={configuration.AdminLoginPath}");
        if (!process.Start()) throw new InvalidOperationException("mysql_config_editor could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("mysql_config_editor rejected the configured F1 login path.");
        _ = ParseOutput(output);
    }

    private sealed class PromptSession : IDisposable
    {
        private readonly SecureString adminPassword = Prompt("F1 administrator password: ");
        private readonly SecureString applicationPassword = Prompt("F1 restricted application password: ");

        public string AdministratorConnection(EmployeeImportMySqlAcceptanceConfiguration configuration, string database) =>
            AddPassword(configuration.ConnectionString ?? throw new InvalidOperationException("The administrator descriptor is missing."), adminPassword, database);

        public string ApplicationConnection(EmployeeImportMySqlAcceptanceConfiguration configuration, string database) =>
            AddPassword(configuration.ApplicationConnectionString, applicationPassword, database);

        public void Dispose()
        {
            adminPassword.Dispose();
            applicationPassword.Dispose();
        }

        private static string AddPassword(string template, SecureString secret, string database)
        {
            var builder = new MySqlConnectionStringBuilder(template) { Database = database, Pooling = false };
            var pointer = IntPtr.Zero;
            try
            {
                pointer = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(secret);
                builder.Password = System.Runtime.InteropServices.Marshal.PtrToStringBSTR(pointer) ?? string.Empty;
                return builder.ConnectionString;
            }
            finally
            {
                if (pointer != IntPtr.Zero) System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(pointer);
            }
        }

        private static SecureString Prompt(string label)
        {
            if (Console.IsInputRedirected) throw new InvalidOperationException("F1 credential prompting requires an interactive local console; redirected input is refused.");
            Console.Error.Write(label);
            var secret = new SecureString();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.RemoveAt(secret.Length - 1); continue; }
                if (!char.IsControl(key.KeyChar)) secret.AppendChar(key.KeyChar);
            }
            Console.Error.WriteLine();
            secret.MakeReadOnly();
            return secret;
        }
    }

    private sealed class Scope(PromptSession? previous, PromptSession session) : IDisposable
    {
        public void Dispose() { session.Dispose(); Current.Value = previous; }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();
        public void Dispose() { }
    }
}
