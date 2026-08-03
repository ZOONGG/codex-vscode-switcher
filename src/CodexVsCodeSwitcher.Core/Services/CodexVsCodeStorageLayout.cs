namespace CodexVsCodeSwitcher.Core.Services;

public sealed class CodexVsCodeStorageLayout
{
    public CodexVsCodeStorageLayout(string userProfile, string localAppData)
    {
        UserProfile = NormalizeRoot(userProfile, nameof(userProfile));
        LocalAppData = NormalizeRoot(localAppData, nameof(localAppData));

        ApplicationDataDirectory = Combine(LocalAppData, "CodexVsCodeSwitcher");
        SettingsFile = Combine(ApplicationDataDirectory, "settings.json");
        ProfilesMetadataFile = Combine(ApplicationDataDirectory, "profiles.json");
        ActiveProfileFile = Combine(ApplicationDataDirectory, "active-profile.txt");
        ProfileStatusFile = Combine(ApplicationDataDirectory, "profile-status.json");
        LogDirectory = Combine(ApplicationDataDirectory, "logs");
        BackupDirectory = Combine(ApplicationDataDirectory, "backups");
        TransactionDirectory = Combine(ApplicationDataDirectory, "transactions");
        RemovedProfilesDirectory = Combine(ApplicationDataDirectory, "removed-profiles");
        VsCodeUserDataDirectory = Combine(ApplicationDataDirectory, "VSCodeData");
        VsCodeExtensionsDirectory = Combine(ApplicationDataDirectory, "VSCodeExtensions");
        VsCodeSharedExtensionsDirectory = Combine(UserProfile, ".vscode", "extensions");
        VsCodeSharedDataDirectory = Combine(ApplicationDataDirectory, "VSCodeSharedData");
        LastWorkspaceMetadataFile = Combine(ApplicationDataDirectory, "last-workspace.json");
        ManagedInstanceMetadataFile = Combine(ApplicationDataDirectory, "managed-vscode.json");
        BridgeDirectory = Combine(ApplicationDataDirectory, "bridge");
        CompanionExtensionDirectory = Combine(ApplicationDataDirectory, "companion-extension");

        ProfilesDirectory = Combine(UserProfile, ".codex-vscode-profiles");
        LegacyProfilesDirectory = Combine(UserProfile, ".codex-profiles");
        ProtectedMainCodexDirectory = Combine(UserProfile, ".codex");
        ProtectedOriginalApplicationDataDirectory = Combine(LocalAppData, "CodexProfileOverlay");
    }

    public string UserProfile { get; }
    public string LocalAppData { get; }
    public string ApplicationDataDirectory { get; }
    public string SettingsFile { get; }
    public string ProfilesMetadataFile { get; }
    public string ActiveProfileFile { get; }
    public string ProfileStatusFile { get; }
    public string LogDirectory { get; }
    public string BackupDirectory { get; }
    public string TransactionDirectory { get; }
    public string RemovedProfilesDirectory { get; }
    public string VsCodeUserDataDirectory { get; }
    public string VsCodeExtensionsDirectory { get; }
    public string VsCodeSharedExtensionsDirectory { get; }
    public string VsCodeSharedDataDirectory { get; }
    public string LastWorkspaceMetadataFile { get; }
    public string ManagedInstanceMetadataFile { get; }
    public string BridgeDirectory { get; }
    public string CompanionExtensionDirectory { get; }
    public string ProfilesDirectory { get; }
    public string LegacyProfilesDirectory { get; }
    public string ProtectedMainCodexDirectory { get; }
    public string ProtectedOriginalApplicationDataDirectory { get; }

    public static CodexVsCodeStorageLayout FromEnvironment()
        => new(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static string NormalizeRoot(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", parameterName);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string Combine(string root, params string[] children)
        => Path.GetFullPath(children.Aggregate(root, Path.Combine));
}
