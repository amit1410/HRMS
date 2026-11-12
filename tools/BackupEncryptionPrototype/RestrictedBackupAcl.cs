using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BackupEncryptionPrototype;

[SupportedOSPlatform("windows")]
internal static class RestrictedBackupAcl
{
    private static readonly SecurityIdentifier SystemSid = new("S-1-5-18");
    private static readonly SecurityIdentifier AdministratorsSid = new("S-1-5-32-544");

    public static void ProtectDirectory(string path)
    {
        EnsureWindows();
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Backup directory is missing or reparse-linked.");
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows identity is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(current);
        foreach (var sid in new[] { current, SystemSid, AdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
        VerifyDirectory(path);
    }

    public static void VerifyDirectory(string path)
    {
        EnsureWindows();
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Backup directory is missing or reparse-linked.");
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows identity is unavailable.");
        var security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (!security.AreAccessRulesProtected) throw new InvalidOperationException("Backup directory ACL inheritance is enabled.");
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var allowed = new[] { current, SystemSid, AdministratorsSid };
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (owner is null || !owner.Equals(current) || rules.Length != allowed.Length || rules.Any(rule => rule.IsInherited ||
            rule.AccessControlType != AccessControlType.Allow || !allowed.Contains((SecurityIdentifier)rule.IdentityReference) ||
            (rule.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl) ||
            allowed.Any(sid => !rules.Any(rule => sid.Equals(rule.IdentityReference))))
            throw new InvalidOperationException("Backup directory ACL is not restricted to the operator, SYSTEM, and local Administrators.");
    }

    public static void VerifyFile(string path)
    {
        EnsureWindows();
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Backup file is missing or reparse-linked.");
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows identity is unavailable.");
        var security = file.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var allowed = new[] { current, SystemSid, AdministratorsSid };
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (owner is null || !owner.Equals(current) || rules.Length != allowed.Length || rules.Any(rule =>
            rule.AccessControlType != AccessControlType.Allow || !allowed.Contains((SecurityIdentifier)rule.IdentityReference) ||
            (rule.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl) ||
            allowed.Any(sid => !rules.Any(rule => sid.Equals(rule.IdentityReference))))
            throw new InvalidOperationException("Backup file ACL is not restricted to the operator, SYSTEM, and local Administrators.");
    }

    public static void ProtectFile(string path)
    {
        EnsureWindows();
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Protected local file is missing or reparse-linked.");
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows identity is unavailable.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(current);
        foreach (var sid in new[] { current, SystemSid, AdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        file.SetAccessControl(security);
        VerifyFile(path);
    }

    public static void VerifyLoginPathFile(string path)
    {
        EnsureWindows();
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("MySQL login-path credential file is unavailable or reparse-linked.");
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows identity is unavailable.");
        var security = file.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (!security.AreAccessRulesProtected) throw new InvalidOperationException("MySQL login-path file must have an explicit protected ACL.");
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var allowed = new[] { current, SystemSid, AdministratorsSid };
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (owner is null || !owner.Equals(current) || rules.Length == 0 || rules.Any(rule => rule.IsInherited ||
            rule.AccessControlType != AccessControlType.Allow || !allowed.Contains((SecurityIdentifier)rule.IdentityReference)) ||
            !rules.Any(rule => current.Equals(rule.IdentityReference) && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl))
            throw new InvalidOperationException("MySQL login-path ACL is broad, inherited, denied, or not operator-owned.");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Restricted backup ACL verification requires Windows.");
    }
}
