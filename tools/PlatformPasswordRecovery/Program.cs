using HRMS.Infrastructure.Security;

namespace HRMS.PlatformPasswordRecovery;

internal static class Program
{
    private const string BackupConfirmation = "BACKUP VERIFIED";
    private const string IdentityConfirmation = "IDENTITY VERIFIED";

    public static int Main(string[] args)
    {
        if (!TryReadArguments(args, out var identity, out var backupReference, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        Console.WriteLine("PLATFORM PASSWORD RECOVERY — OFFLINE DRY RUN ONLY");
        Console.WriteLine("This utility performs no database, network, SMTP, API, migration, or file writes.");
        Console.WriteLine($"Target user ID: {identity.UserId}");
        Console.WriteLine($"Target email:   {identity.Email}");
        Console.Write("Type the target user ID exactly to confirm identity: ");
        if (!string.Equals(Console.ReadLine(), identity.UserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Exact account identity confirmation failed.");
            return 3;
        }
        Console.Write("Type the target email exactly to confirm identity: ");
        if (!string.Equals(Console.ReadLine(), identity.Email, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Exact account identity confirmation failed.");
            return 3;
        }

        Console.Write($"Type '{IdentityConfirmation}' to confirm this is the verified active PlatformSuperAdmin: ");
        if (!string.Equals(Console.ReadLine(), IdentityConfirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("PlatformSuperAdmin confirmation failed.");
            return 3;
        }

        Console.WriteLine($"Backup reference: {backupReference}");
        Console.Write($"Type '{BackupConfirmation}' only after independently verifying the backup is restorable: ");
        if (!string.Equals(Console.ReadLine(), BackupConfirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Restorable-backup confirmation failed.");
            return 3;
        }

        var password = ReadHidden("New password: ");
        var confirmation = ReadHidden("Confirm new password: ");
        try
        {
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Password confirmation failed.");
                return 4;
            }

            var validation = PasswordPolicy.Validate(password);
            if (validation is not null)
            {
                Console.Error.WriteLine(validation);
                return 4;
            }

            // Reuse the production hasher. The resulting hash is deliberately never printed,
            // persisted, passed to a child process, or logged. This project has no write path.
            _ = new IdentityPasswordHasher().Hash(password);
        }
        finally
        {
            password = string.Empty;
            confirmation = string.Empty;
        }

        Console.WriteLine();
        Console.WriteLine("DRY RUN PASSED. Proposed future scope:");
        Console.WriteLine("- update exactly this PlatformUser PasswordHash using IdentityPasswordHasher;");
        Console.WriteLine("- increment SecurityRevision exactly once;");
        Console.WriteLine("- revoke all active PlatformRefreshTokens for this user;");
        Console.WriteLine("- commit those changes in one transaction.");
        Console.WriteLine("No database write was attempted. Migration history is out of scope.");
        return 0;
    }

    private static bool TryReadArguments(string[] args, out VerifiedIdentity identity, out string backupReference, out string error)
    {
        identity = default;
        backupReference = string.Empty;
        error = "Usage: PlatformPasswordRecovery --user-id <GUID> --email <exact-email> --backup-reference <non-secret-reference>";
        if (args.Length != 6) return false;

        Guid? userId = null;
        string? email = null;
        for (var i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--user-id" or "--email" or "--backup-reference") || string.IsNullOrWhiteSpace(args[i + 1])) return false;
            switch (args[i])
            {
                case "--user-id" when Guid.TryParse(args[i + 1], out var parsed): userId = parsed; break;
                case "--email": email = args[i + 1].Trim(); break;
                case "--backup-reference": backupReference = args[i + 1].Trim(); break;
                default: return false;
            }
        }

        if (userId is null || string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal) || string.IsNullOrWhiteSpace(backupReference)) return false;
        identity = new VerifiedIdentity(userId.Value, email);
        error = string.Empty;
        return true;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key is ConsoleKey.Enter) break;
            if (key.Key is ConsoleKey.Backspace)
            {
                if (value.Length > 0) value.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
        Console.WriteLine();
        return value.ToString();
    }
}

internal readonly record struct VerifiedIdentity(Guid UserId, string Email);

internal static class PasswordPolicy
{
    public static string? Validate(string password)
    {
        if (password.Length < 14) return "Password must contain at least 14 characters.";
        if (password.Any(char.IsWhiteSpace)) return "Password must not contain whitespace.";
        if (!password.Any(char.IsUpper)) return "Password must contain an uppercase letter.";
        if (!password.Any(char.IsLower)) return "Password must contain a lowercase letter.";
        if (!password.Any(char.IsDigit)) return "Password must contain a digit.";
        if (!password.Any(c => !char.IsLetterOrDigit(c))) return "Password must contain a symbol.";
        return null;
    }
}
