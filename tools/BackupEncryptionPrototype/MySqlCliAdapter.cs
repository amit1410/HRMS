using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace BackupEncryptionPrototype;

public sealed record MySqlCliProfile(string MysqlExe, string MysqldumpExe, string Host, int Port,
    string LoginPath, string CaPemPath, string TlsMode, string? LoginPathFile = null);

public sealed class MySqlCliException(string stage, int? exitCode, string category, Exception? inner = null)
    : IOException($"MySQL client failed at {stage} ({category}, exit={exitCode?.ToString() ?? "n/a"}).", inner)
{
    public string Stage { get; } = stage;
    public int? ExitCode { get; } = exitCode;
    public string Category { get; } = category;
}

/// <summary>MySQL 8 command adapter: password material is read only from a restricted mysql_config_editor login path.</summary>
[SupportedOSPlatform("windows")]
public sealed class MySqlCliAdapter
{
    private const int QueryOutputLimit = 256 * 1024;
    private readonly TimeSpan _timeout;

    public MySqlCliAdapter(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromMinutes(5);

    internal async Task<IReadOnlyList<string>> QueryAsync(MySqlCliProfile profile, string database, string sql,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile, database);
        var query = sql?.Trim() ?? string.Empty;
        if (query.Length is 0 or > 8192 || query.Contains(';') ||
            !(query.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("SHOW ", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Only one bounded read-only SELECT or SHOW statement is supported.", nameof(sql));
        var start = CreateStartInfo(profile, profile.MysqlExe);
        foreach (var arg in new[] { "--batch", "--raw", "--skip-column-names", "--execute=" + query, database }) start.ArgumentList.Add(arg);
        var bytes = await RunCapturedAsync(start, "mysql.query", cancellationToken).ConfigureAwait(false);
        try
        {
            var output = new UTF8Encoding(false, true).GetString(bytes);
            return output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (DecoderFallbackException ex) { throw new MySqlCliException("mysql.query", null, "invalid_utf8_output", ex); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public MySqlDumpProcess StartDump(MySqlCliProfile profile, string database, bool schemaOnly = false)
    {
        ValidateProfile(profile, database);
        if (database != "hrms_web1" && !(schemaOnly && database.StartsWith("hrms_restore_", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Full dumps are restricted to hrms_web1; schema-only inspection may target an isolated restore schema.");
        var start = CreateStartInfo(profile, profile.MysqldumpExe);
        foreach (var arg in new[]
        {
            "--single-transaction", "--quick", "--routines", "--events", "--triggers", "--hex-blob",
            "--set-gtid-purged=OFF", "--no-tablespaces", "--skip-comments"
        }) start.ArgumentList.Add(arg);
        if (schemaOnly) start.ArgumentList.Add("--no-data");
        start.ArgumentList.Add(database);
        try
        {
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            if (!process.Start()) throw new MySqlCliException("mysqldump.start", null, "process_start_returned_false");
            return new MySqlDumpProcess(process, _timeout);
        }
        catch (MySqlCliException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        { throw new MySqlCliException("mysqldump.start", null, SafeProcessCategory(ex), ex); }
    }

    internal MySqlSqlInputProcess StartSqlInput(MySqlCliProfile profile, string database)
    {
        ValidateProfile(profile, database);
        var start = CreateStartInfo(profile, profile.MysqlExe);
        start.RedirectStandardInput = true;
        start.ArgumentList.Add("--binary-mode");
        start.ArgumentList.Add("--database=" + database);
        try
        {
            var process = new Process { StartInfo = start };
            if (!process.Start()) throw new MySqlCliException("mysql.stdin.start", null, "process_start_returned_false");
            return new MySqlSqlInputProcess(process, _timeout);
        }
        catch (MySqlCliException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        { throw new MySqlCliException("mysql.stdin.start", null, SafeProcessCategory(ex), ex); }
    }

    private async Task<byte[]> RunCapturedAsync(ProcessStartInfo start, string stage, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new MySqlCliException(stage, null, "process_start_returned_false");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            var stdoutTask = ReadBoundedAsync(process.StandardOutput.BaseStream, QueryOutputLimit, timeout.Token);
            var stderrTask = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                TryTerminate(process);
                throw new MySqlCliException(stage, null, cancellationToken.IsCancellationRequested ? "cancelled" : "timeout");
            }
            var output = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                CryptographicOperations.ZeroMemory(output);
                throw new MySqlCliException(stage, process.ExitCode, "client_exit_nonzero");
            }
            return output;
        }
        catch (MySqlCliException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
        { throw new MySqlCliException(stage, process.HasExited ? process.ExitCode : null, SafeProcessCategory(ex), ex); }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, int maxBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) return output.ToArray();
                if (output.Length + read > maxBytes) throw new MySqlCliException("mysql.query", null, "query_output_limit");
                output.Write(buffer, 0, read);
            }
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private static ProcessStartInfo CreateStartInfo(MySqlCliProfile profile, string executable)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executable),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // MySQL clients read the protected .mylogin.cnf even with --no-defaults.
        start.ArgumentList.Add("--no-defaults");
        start.ArgumentList.Add("--login-path=" + profile.LoginPath);
        start.ArgumentList.Add("--host=" + profile.Host);
        start.ArgumentList.Add("--port=" + profile.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--ssl-mode=" + ToClientTlsMode(profile.TlsMode));
        start.ArgumentList.Add("--ssl-ca=" + Path.GetFullPath(profile.CaPemPath));
        start.ArgumentList.Add("--default-character-set=utf8mb4");
        if (!string.IsNullOrWhiteSpace(profile.LoginPathFile)) start.Environment["MYSQL_TEST_LOGIN_FILE"] = Path.GetFullPath(profile.LoginPathFile);
        return start;
    }

    internal static string ToClientTlsMode(string mode) => mode switch
    {
        "VerifyCA" => "VERIFY_CA",
        "VerifyFull" => "VERIFY_IDENTITY",
        _ => throw new InvalidOperationException("MySQL TLS must verify the configured CA and may not be downgraded.")
    };

    private static void ValidateProfile(MySqlCliProfile profile, string database)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Path.IsPathFullyQualified(profile.MysqlExe) || !File.Exists(profile.MysqlExe) ||
            !Path.IsPathFullyQualified(profile.MysqldumpExe) || !File.Exists(profile.MysqldumpExe))
            throw new InvalidOperationException("MySQL client executable paths are not verified.");
        var mysqlVersion = FileVersionInfo.GetVersionInfo(profile.MysqlExe).FileVersion ?? string.Empty;
        var dumpVersion = FileVersionInfo.GetVersionInfo(profile.MysqldumpExe).FileVersion ?? string.Empty;
        if ((File.GetAttributes(profile.MysqlExe) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(profile.MysqldumpExe) & FileAttributes.ReparsePoint) != 0 ||
            !Path.GetDirectoryName(Path.GetFullPath(profile.MysqlExe))!.Equals(
                Path.GetDirectoryName(Path.GetFullPath(profile.MysqldumpExe)), StringComparison.OrdinalIgnoreCase) ||
            !mysqlVersion.StartsWith("8.0.46", StringComparison.Ordinal) || !dumpVersion.StartsWith("8.0.46", StringComparison.Ordinal))
            throw new InvalidOperationException("MySQL 8.0.46 client and dump binaries must be regular files from the same installation directory.");
        if (!IPAddress.TryParse(profile.Host, out var address) || !IPAddress.IsLoopback(address) || profile.Port is < 1 or > 65535)
            throw new InvalidOperationException("Backup tooling requires an explicit loopback endpoint and valid port.");
        if (profile.TlsMode is not ("VerifyCA" or "VerifyFull")) throw new InvalidOperationException("MySQL TLS must verify the configured CA and may not be downgraded.");
        if (profile.LoginPath.Length is < 1 or > 64 || profile.LoginPath.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new InvalidOperationException("MySQL login-path name is invalid.");
        if (database is not ("hrms_catalog" or "hrms_web1") && !database.StartsWith("hrms_restore_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Database is outside the approved catalog, WEB1, or isolated-restore scope.");
        var caPath = Path.GetFullPath(profile.CaPemPath);
        if (!File.Exists(caPath) || (File.GetAttributes(caPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Approved MySQL CA certificate is unavailable or reparse-linked.");
        var loginFile = string.IsNullOrWhiteSpace(profile.LoginPathFile)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MySQL", ".mylogin.cnf")
            : Path.GetFullPath(profile.LoginPathFile);
        RestrictedBackupAcl.VerifyLoginPathFile(loginFile);
    }

    private static string SafeProcessCategory(Exception ex) => ex switch
    {
        System.ComponentModel.Win32Exception => "process_access_or_launch_error",
        OperationCanceledException => "cancelled",
        _ => "process_io_error"
    };

    private static void TryTerminate(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

public sealed class MySqlDumpProcess : IDisposable
{
    private readonly Process _process;
    private readonly TimeSpan _timeout;
    private readonly Task _stderrDrain;
    private bool _completed;
    public Stream Output => _process.StandardOutput.BaseStream;

    internal MySqlDumpProcess(Process process, TimeSpan timeout)
    {
        _process = process;
        _timeout = timeout;
        _stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
    }

    public int CompleteAndGetExitCode()
    {
        if (_completed) throw new InvalidOperationException("mysqldump completion can be observed only once.");
        try
        {
            using var timeout = new CancellationTokenSource(_timeout);
            _process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            _stderrDrain.GetAwaiter().GetResult();
            _completed = true;
            return _process.ExitCode;
        }
        catch (OperationCanceledException ex)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
            throw new MySqlCliException("mysqldump.complete", null, "timeout", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        { throw new MySqlCliException("mysqldump.complete", _process.HasExited ? _process.ExitCode : null, "process_wait_failed", ex); }
    }

    public void Dispose()
    {
        if (!_completed)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
        }
        _process.Dispose();
    }
}

public sealed class MySqlSqlInputProcess : IDisposable
{
    private readonly Process _process;
    private readonly TimeSpan _timeout;
    private readonly Task _stdoutDrain;
    private readonly Task _stderrDrain;
    private bool _completed;
    public Stream Input => _process.StandardInput.BaseStream;

    internal MySqlSqlInputProcess(Process process, TimeSpan timeout)
    {
        _process = process;
        _timeout = timeout;
        _stdoutDrain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        _stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
    }

    public void Complete()
    {
        if (_completed) throw new InvalidOperationException("MySQL stdin completion can be observed only once.");
        try
        {
            using var timeout = new CancellationTokenSource(_timeout);
            _process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            Task.WhenAll(_stdoutDrain, _stderrDrain).GetAwaiter().GetResult();
            _completed = true;
            if (_process.ExitCode != 0) throw new MySqlCliException("mysql.restore", _process.ExitCode, "client_exit_nonzero");
        }
        catch (MySqlCliException) { throw; }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
            throw new MySqlCliException("mysql.restore", _process.HasExited ? _process.ExitCode : null,
                ex is OperationCanceledException ? "timeout" : "process_wait_failed", ex);
        }
    }

    public void Abort()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
        try { _process.WaitForExit(); } catch { }
        _completed = true;
    }

    public void Dispose()
    {
        if (!_completed) Abort();
        _process.Dispose();
    }
}
