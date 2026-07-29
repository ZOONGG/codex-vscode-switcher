using System.Text.Json;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ProfileStorageAuditService
{
    public const long MaximumAuthenticationFileBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sessions",
        "archived_sessions",
        "logs",
        "log",
        "cache",
        "caches",
        "attachments",
        "temp",
        "tmp",
    };

    private static readonly HashSet<string> IgnoredFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db",
        ".db-shm",
        ".db-wal",
        ".sqlite",
        ".sqlite3",
        ".log",
        ".tmp",
    };

    private readonly string profilesDirectory;
    private readonly IProtectedPathPolicy protectedPaths;

    public ProfileStorageAuditService(string profilesDirectory, IProtectedPathPolicy protectedPaths)
    {
        this.profilesDirectory = Path.GetFullPath(profilesDirectory);
        this.protectedPaths = protectedPaths;
    }

    public IReadOnlyList<ProfileStorageAudit> AuditProfiles()
    {
        if (!Directory.Exists(profilesDirectory))
        {
            return Array.Empty<ProfileStorageAudit>();
        }

        protectedPaths.AssertCanRead(profilesDirectory);
        return Directory.EnumerateDirectories(profilesDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(static directory => !IsReparsePoint(directory))
            .Select(AuditProfile)
            .Where(static audit => audit is not null)
            .Cast<ProfileStorageAudit>()
            .OrderBy(static audit => audit.ProfileName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public ProfileStorageAudit AuditRequiredProfile(string profileName)
    {
        string validName = ProfileName.RequireValid(profileName);
        string directory = Path.GetFullPath(Path.Combine(profilesDirectory, validName));
        EnsureInsideProfilesRoot(directory);
        protectedPaths.AssertCanRead(directory);
        if (!Directory.Exists(directory) || IsReparsePoint(directory))
        {
            throw new DirectoryNotFoundException("The isolated profile directory was not found.");
        }

        return AuditProfile(directory)
            ?? throw new InvalidOperationException("The isolated profile directory name is invalid.");
    }

    public ProfileCleanupPlan CreateFutureCleanupPlan(string profileName)
    {
        ProfileStorageAudit audit = AuditRequiredProfile(profileName);
        return new ProfileCleanupPlan(
            audit.ProfileName,
            audit.DirectoryPath,
            audit.IgnoredRuntimeFileCount,
            RequiresExplicitConfirmation: true,
            CanExecute: false);
    }

    private ProfileStorageAudit? AuditProfile(string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        EnsureInsideProfilesRoot(fullDirectory);
        protectedPaths.AssertCanRead(fullDirectory);
        string name = Path.GetFileName(fullDirectory);
        if (!ProfileName.IsValid(name))
        {
            return null;
        }

        string authFile = Path.Combine(fullDirectory, "auth.json");
        string configFile = Path.Combine(fullDirectory, "config.toml");
        bool hasAuth = File.Exists(authFile) && !IsReparsePoint(authFile);
        bool hasConfig = File.Exists(configFile) && !IsReparsePoint(configFile);
        ProfileValidationStatus status = !hasAuth
            ? ProfileValidationStatus.Incomplete
            : HasValidAuthenticationJson(authFile)
                ? ProfileValidationStatus.Valid
                : ProfileValidationStatus.Invalid;
        (long directorySize, int ignoredFiles) = MeasureDirectory(fullDirectory);
        return new ProfileStorageAudit(
            name,
            fullDirectory,
            status,
            directorySize,
            ignoredFiles,
            hasAuth,
            hasConfig);
    }

    private bool HasValidAuthenticationJson(string authFile)
    {
        protectedPaths.AssertCanRead(authFile);
        var info = new FileInfo(authFile);
        if (!info.Exists || info.Length <= 1 || info.Length > MaximumAuthenticationFileBytes)
        {
            return false;
        }

        try
        {
            using FileStream stream = new(
                authFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            using JsonDocument document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static (long DirectorySize, int IgnoredFiles) MeasureDirectory(string root)
    {
        long size = 0;
        int ignoredFiles = 0;
        var pending = new Stack<(string Directory, bool RuntimeData)>();
        pending.Push((root, false));
        while (pending.Count > 0)
        {
            (string directory, bool runtimeData) = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory
                    .EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly)
                    .ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string entry in entries)
            {
                if (IsReparsePoint(entry))
                {
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    bool ignoredDirectory = runtimeData || IgnoredDirectoryNames.Contains(Path.GetFileName(entry));
                    pending.Push((entry, ignoredDirectory));
                    continue;
                }

                try
                {
                    var file = new FileInfo(entry);
                    size = checked(size + file.Length);
                    if (runtimeData || IsIgnoredRuntimeFile(file))
                    {
                        ignoredFiles++;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (OverflowException)
                {
                    size = long.MaxValue;
                }
            }
        }

        return (size, ignoredFiles);
    }

    private static bool IsIgnoredRuntimeFile(FileInfo file)
        => (file.Name.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase)
            && file.Extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
            || IgnoredFileExtensions.Contains(file.Extension)
            || file.Length > MaximumAuthenticationFileBytes;

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void EnsureInsideProfilesRoot(string fullPath)
    {
        string root = Path.TrimEndingDirectorySeparator(profilesDirectory) + Path.DirectorySeparatorChar;
        string candidate = Path.TrimEndingDirectorySeparator(fullPath) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved profile path escapes the profiles directory.");
        }
    }
}

public sealed record ProfileCleanupPlan(
    string ProfileName,
    string DirectoryPath,
    int RuntimeFileCount,
    bool RequiresExplicitConfirmation,
    bool CanExecute);
