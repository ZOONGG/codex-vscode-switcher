using CodexProfileOverlay.Core.Models;

namespace CodexProfileOverlay.Core.Services;

public sealed class AuthSwitchService
{
    private const string ProfileStateDirectoryName = "codex-state";

    private static readonly string[] ManagedStateFiles =
    [
        ".codex-global-state.json",
        ".codex-global-state.json.bak",
        "session_index.jsonl",
    ];

    private static readonly string[] ManagedStateFilePatterns =
    [
        "state_*.sqlite*",
        "goals_*.sqlite*",
        "memories_*.sqlite*",
    ];

    private static readonly string[] ManagedStateDirectories =
    [
        "archived_sessions",
        "attachments",
        "sessions",
    ];

    private readonly AppPaths paths;
    private readonly ProfileDiscoveryService profileDiscovery;
    private readonly ActiveProfileStore activeProfileStore;
    private readonly IAtomicFileReplacer replacer;
    private readonly SemaphoreSlim switchGate = new(1, 1);

    public AuthSwitchService(
        AppPaths paths,
        ProfileDiscoveryService profileDiscovery,
        ActiveProfileStore activeProfileStore,
        IAtomicFileReplacer? replacer = null)
    {
        this.paths = paths;
        this.profileDiscovery = profileDiscovery;
        this.activeProfileStore = activeProfileStore;
        this.replacer = replacer ?? new AtomicFileReplacer();
    }

    public async Task<AuthSwitchResult> SwitchAsync(string targetProfileName, CancellationToken cancellationToken = default)
    {
        if (!await switchGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A profile switch is already in progress.");
        }

        string? backupPath = null;
        string? stateBackupDirectory = null;
        string? previousProfile = null;

        try
        {
            previousProfile = activeProfileStore.Read();
            var targetProfile = profileDiscovery.GetRequiredProfile(targetProfileName);
            if (string.Equals(previousProfile, targetProfile.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected profile is already active.");
            }

            if (previousProfile is not null && File.Exists(paths.SharedAuthFile))
            {
                var currentProfile = profileDiscovery.GetRequiredProfile(previousProfile);
                File.Copy(paths.SharedAuthFile, currentProfile.AuthFilePath, overwrite: true);
                SaveSharedCodexState(currentProfile);
            }

            if (!File.Exists(targetProfile.AuthFilePath))
            {
                throw new FileNotFoundException($"Profile '{targetProfile.Name}' does not contain auth.json.", targetProfile.AuthFilePath);
            }

            ValidateReadable(targetProfile.AuthFilePath);

            if (File.Exists(paths.SharedAuthFile))
            {
                Directory.CreateDirectory(paths.BackupDirectory);
                backupPath = Path.Combine(
                    paths.BackupDirectory,
                    $"auth-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
                File.Copy(paths.SharedAuthFile, backupPath, overwrite: false);
            }

            stateBackupDirectory = BackupSharedCodexStateIfPresent();

            try
            {
                replacer.ReplaceFromSource(targetProfile.AuthFilePath, paths.SharedAuthFile);
                InstallProfileCodexState(targetProfile);
                activeProfileStore.Write(targetProfile.Name);
            }
            catch
            {
                RestoreBackupIfPossible(backupPath);
                RestoreStateBackupIfPossible(stateBackupDirectory);
                throw;
            }

            return new AuthSwitchResult(targetProfile.Name, previousProfile, backupPath);
        }
        finally
        {
            switchGate.Release();
        }
    }

    private void SaveSharedCodexState(ProfileInfo profile)
    {
        string profileStateDirectory = GetProfileStateDirectory(profile);
        ReplaceProfileStateFromShared(profileStateDirectory);
    }

    private void InstallProfileCodexState(ProfileInfo profile)
    {
        string profileStateDirectory = GetProfileStateDirectory(profile);
        ClearManagedState(paths.SharedCodexDirectory);

        if (!Directory.Exists(profileStateDirectory) || !Directory.EnumerateFileSystemEntries(profileStateDirectory).Any())
        {
            return;
        }

        CopyDirectoryContents(profileStateDirectory, paths.SharedCodexDirectory);
    }

    private string? BackupSharedCodexStateIfPresent()
    {
        if (!Directory.Exists(paths.SharedCodexDirectory) || !EnumerateManagedStateItems(paths.SharedCodexDirectory).Any())
        {
            return null;
        }

        Directory.CreateDirectory(paths.BackupDirectory);
        string backupDirectory = Path.Combine(
            paths.BackupDirectory,
            $"state-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
        ReplaceProfileStateFromShared(backupDirectory);
        return backupDirectory;
    }

    private void RestoreStateBackupIfPossible(string? backupDirectory)
    {
        ClearManagedState(paths.SharedCodexDirectory);
        if (backupDirectory is null || !Directory.Exists(backupDirectory))
        {
            return;
        }

        CopyDirectoryContents(backupDirectory, paths.SharedCodexDirectory);
    }

    private void ReplaceProfileStateFromShared(string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        if (!Directory.Exists(paths.SharedCodexDirectory))
        {
            return;
        }

        var stateItems = EnumerateManagedStateItems(paths.SharedCodexDirectory).ToArray();
        if (stateItems.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourcePath in stateItems)
        {
            string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            if (Directory.Exists(sourcePath))
            {
                CopyDirectory(sourcePath, destinationPath);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
        }
    }

    private void ClearManagedState(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        foreach (string path in EnumerateManagedStateItems(rootDirectory).OrderByDescending(static path => path.Length))
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static IEnumerable<string> EnumerateManagedStateItems(string rootDirectory)
    {
        foreach (string fileName in ManagedStateFiles)
        {
            string path = Path.Combine(rootDirectory, fileName);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        foreach (string pattern in ManagedStateFilePatterns)
        {
            foreach (string path in Directory.EnumerateFiles(rootDirectory, pattern, SearchOption.TopDirectoryOnly))
            {
                yield return path;
            }
        }

        foreach (string directoryName in ManagedStateDirectories)
        {
            string path = Path.Combine(rootDirectory, directoryName);
            if (Directory.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            if (Directory.Exists(sourcePath))
            {
                CopyDirectory(sourcePath, destinationPath);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            if (Directory.Exists(sourcePath))
            {
                CopyDirectory(sourcePath, destinationPath);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
        }
    }

    private static string GetProfileStateDirectory(ProfileInfo profile)
    {
        return Path.Combine(profile.DirectoryPath, ProfileStateDirectoryName);
    }

    private void RestoreBackupIfPossible(string? backupPath)
    {
        if (backupPath is null || !File.Exists(backupPath))
        {
            return;
        }

        new AtomicFileReplacer().ReplaceFromSource(backupPath, paths.SharedAuthFile);
    }

    private static void ValidateReadable(string path)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0)
        {
            throw new InvalidDataException("Target auth.json is empty.");
        }
    }
}
