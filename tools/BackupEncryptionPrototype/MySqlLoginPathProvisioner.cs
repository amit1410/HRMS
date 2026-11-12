using System.Diagnostics;

namespace BackupEncryptionPrototype;

/// <summary>Interactive credential bootstrap. Password is accepted only by mysql_config_editor's prompt.</summary>
public static class MySqlLoginPathProvisioner
{
    public static async Task ProvisionAsync(string mysqlConfigEditorExe, string loginPath, string host, int port,
        string databaseUser, string protectedCredentialDirectory, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This local credential bootstrap requires Windows ACLs.");
        if (!Path.IsPathFullyQualified(mysqlConfigEditorExe) || !File.Exists(mysqlConfigEditorExe))
            throw new InvalidOperationException("mysql_config_editor executable path is not verified.");
        if (!System.Net.IPAddress.TryParse(host, out var address) || !System.Net.IPAddress.IsLoopback(address) || port is < 1 or > 65535)
            throw new InvalidOperationException("Credential paths are limited to an explicit loopback server endpoint.");
        if (string.IsNullOrWhiteSpace(loginPath) || loginPath.Length > 64 || loginPath.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new ArgumentException("Login-path name is invalid.", nameof(loginPath));
        if (string.IsNullOrWhiteSpace(databaseUser) || databaseUser.Any(char.IsWhiteSpace) || databaseUser.Contains('=') || databaseUser.Contains(';'))
            throw new ArgumentException("Database username format is invalid.", nameof(databaseUser));

        var directory = Path.GetFullPath(protectedCredentialDirectory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Credential directory already exists; bootstrap will not overwrite it.");
        StreamingArtifactPublisher.InitializeNewProtectedRoot(directory);
        var loginFile = Path.Combine(directory, ".mylogin.cnf");
        var start = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(mysqlConfigEditorExe),
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["MYSQL_TEST_LOGIN_FILE"] = loginFile;
        foreach (var arg in new[] { "set", "--login-path=" + loginPath, "--host=" + host, "--port=" + port,
                     "--user=" + databaseUser, "--password" })
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new MySqlCliException("mysql_config_editor.start", null, "process_start_returned_false");
            var stdoutDrain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
            var stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutDrain, stderrDrain).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new MySqlCliException("mysql_config_editor.set", process.ExitCode, "credential_helper_failed");
            RestrictedBackupAcl.ProtectFile(loginFile);
            RestrictedBackupAcl.VerifyLoginPathFile(loginFile);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { if (File.Exists(loginFile)) File.Delete(loginFile); } catch { }
            throw;
        }
    }
}
