using System.Security.AccessControl;
using System.Security.Principal;

namespace SummonersVault.Infrastructure.Storage;

public sealed class VaultPaths
{
    public VaultPaths(string? rootDirectory = null)
    {
        string applicationRoot = rootDirectory is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SummonersVault")
            : rootDirectory;
        RootDirectory = rootDirectory is null ? Path.Combine(applicationRoot, "Data") : applicationRoot;
        ArtworkCacheDirectory = Path.Combine(applicationRoot, "Cache", "Artwork");
        DatabasePath = Path.Combine(RootDirectory, "vault.db");
        MetadataPath = Path.Combine(RootDirectory, "vault.meta.json");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
    }

    public string RootDirectory { get; }
    public string DatabasePath { get; }
    public string MetadataPath { get; }
    public string SettingsPath { get; }
    public string ArtworkCacheDirectory { get; }

    public void EnsureCreated()
    {
        DirectoryInfo directory = Directory.CreateDirectory(RootDirectory);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier sid = identity.User ?? throw new InvalidOperationException("The current Windows user could not be identified.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }


    public void EnsureArtworkCacheCreated() => RestrictDirectory(Directory.CreateDirectory(ArtworkCacheDirectory));

    private static void RestrictDirectory(DirectoryInfo directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier sid = identity.User ?? throw new InvalidOperationException("The current Windows user could not be identified.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}
