using System.Security.Cryptography;
using System.Text.Json;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class MinimalBackupService
{
    public const long MaximumFileBytes = 10L * 1024 * 1024;
    public const long MaximumTransactionBytes = 25L * 1024 * 1024;
    public const int MaximumCompletedBackups = 5;
    public const long MaximumRetainedBytes = 100L * 1024 * 1024;

    private static readonly HashSet<string> AllowedFileNames =
        new(StringComparer.OrdinalIgnoreCase) { "auth.json", "config.toml" };

    private readonly CodexVsCodeStorageLayout layout;
    private readonly IProtectedPathPolicy protectedPaths;
    private readonly SafeLogger? logger;

    public MinimalBackupService(
        CodexVsCodeStorageLayout layout,
        IProtectedPathPolicy protectedPaths,
        SafeLogger? logger = null)
    {
        this.layout = layout;
        this.protectedPaths = protectedPaths;
        this.logger = logger;
    }

    public string CreateCompletedBackup(string profileId, IReadOnlyCollection<string> explicitFiles)
    {
        string safeProfileId = ProfileName.RequireValid(profileId);
        ArgumentNullException.ThrowIfNull(explicitFiles);
        if (explicitFiles.Count == 0)
        {
            throw new ArgumentException("At least one explicitly allowlisted file is required.", nameof(explicitFiles));
        }

        Directory.CreateDirectory(layout.TransactionDirectory);
        Directory.CreateDirectory(layout.BackupDirectory);
        protectedPaths.AssertCanWrite(layout.TransactionDirectory);
        protectedPaths.AssertCanWrite(layout.BackupDirectory);

        string transaction = Path.Combine(layout.TransactionDirectory, $"txn-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        protectedPaths.AssertCanWrite(transaction);
        Directory.CreateDirectory(transaction);

        try
        {
            var entries = new List<MinimalBackupEntry>();
            long totalBytes = 0;
            foreach (string source in explicitFiles)
            {
                string fullSource = Path.GetFullPath(source);
                string fileName = Path.GetFileName(fullSource);
                if (!AllowedFileNames.Contains(fileName))
                {
                    throw new InvalidOperationException($"'{fileName}' is not allowlisted for backup.");
                }

                EnsureInsideProfileRoot(fullSource);
                protectedPaths.AssertCanCopy(fullSource, Path.Combine(transaction, fileName));

                var info = new FileInfo(fullSource);
                if (!info.Exists)
                {
                    throw new FileNotFoundException("The requested backup file does not exist.", fullSource);
                }

                if (info.Length > MaximumFileBytes)
                {
                    throw new InvalidOperationException("A backup input exceeds the 10 MB per-file limit.");
                }

                totalBytes = checked(totalBytes + info.Length);
                if (totalBytes > MaximumTransactionBytes)
                {
                    throw new InvalidOperationException("The backup exceeds the 25 MB transaction limit.");
                }

                string destination = Path.Combine(transaction, fileName);
                File.Copy(fullSource, destination, overwrite: false);
                entries.Add(new MinimalBackupEntry(fileName, info.Length, ComputeSha256(destination)));
            }

            var manifest = new MinimalBackupManifest(
                1,
                safeProfileId,
                DateTimeOffset.UtcNow,
                totalBytes,
                entries);
            string manifestPath = Path.Combine(transaction, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            string completed = Path.Combine(layout.BackupDirectory, $"completed-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
            protectedPaths.AssertCanWrite(completed);
            Directory.Move(transaction, completed);
            CleanupRetention();
            logger?.Info($"Created minimal profile backup with {entries.Count} allowlisted file(s), {totalBytes} bytes.");
            return completed;
        }
        catch
        {
            if (Directory.Exists(transaction))
            {
                Directory.Delete(transaction, recursive: true);
            }

            throw;
        }
    }

    public BackupStorageSummary GetStorageSummary()
    {
        IReadOnlyList<DirectoryInfo> completed = GetCompletedDirectories();
        return new BackupStorageSummary(
            completed.Sum(GetDirectorySize),
            completed.Count,
            MaximumCompletedBackups,
            MaximumRetainedBytes);
    }

    public void CleanupRetention()
    {
        List<DirectoryInfo> completed = GetCompletedDirectories()
            .OrderByDescending(static directory => directory.CreationTimeUtc)
            .ToList();
        long retained = 0;
        for (int index = 0; index < completed.Count; index++)
        {
            DirectoryInfo directory = completed[index];
            long size = GetDirectorySize(directory);
            bool retain = index < MaximumCompletedBackups && retained + size <= MaximumRetainedBytes;
            if (retain)
            {
                retained += size;
                continue;
            }

            protectedPaths.AssertCanWrite(directory.FullName);
            directory.Delete(recursive: true);
        }
    }

    public void CleanAllCompleted()
    {
        foreach (DirectoryInfo directory in GetCompletedDirectories())
        {
            protectedPaths.AssertCanWrite(directory.FullName);
            directory.Delete(recursive: true);
        }
    }

    private IReadOnlyList<DirectoryInfo> GetCompletedDirectories()
    {
        if (!Directory.Exists(layout.BackupDirectory))
        {
            return Array.Empty<DirectoryInfo>();
        }

        return new DirectoryInfo(layout.BackupDirectory)
            .EnumerateDirectories("completed-*", SearchOption.TopDirectoryOnly)
            .Where(static directory => (directory.Attributes & FileAttributes.ReparsePoint) == 0)
            .ToArray();
    }

    private void EnsureInsideProfileRoot(string path)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(layout.ProfilesDirectory));
        string candidate = Path.GetFullPath(path);
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Backup inputs must be files inside the dedicated VS Code profile root.");
        }
    }

    private static long GetDirectorySize(DirectoryInfo directory)
        => directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly).Sum(static file => file.Length);

    private static string ComputeSha256(string file)
    {
        using FileStream stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

public sealed record MinimalBackupManifest(
    int SchemaVersion,
    string ProfileId,
    DateTimeOffset CreatedAt,
    long TotalBytes,
    IReadOnlyList<MinimalBackupEntry> Files);

public sealed record MinimalBackupEntry(string FileName, long Size, string Sha256);

public sealed record BackupStorageSummary(
    long TotalBytes,
    int CompletedBackupCount,
    int RetentionCountLimit,
    long RetentionStorageLimitBytes);
