using System.Text.Json;
using System.Text.Json.Serialization;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class WorkspaceHistoryService : IWorkspaceHistoryService
{
    public const int MaximumRecentProjects = 10;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string filePath;
    private readonly IProtectedPathPolicy protectedPaths;
    private readonly object gate = new();

    public WorkspaceHistoryService(string filePath, IProtectedPathPolicy protectedPaths)
    {
        this.filePath = Path.GetFullPath(filePath);
        this.protectedPaths = protectedPaths;
        protectedPaths.AssertCanWrite(this.filePath);
    }

    public string? ReadLastWorkspace() => ReadSnapshot().CurrentProject?.Path;

    public WorkspaceHistorySnapshot ReadSnapshot()
    {
        lock (gate)
        {
            WorkspaceHistoryDocument document = ReadDocument();
            return new WorkspaceHistorySnapshot(document.CurrentProject, document.RecentProjects);
        }
    }

    public void SaveLastWorkspace(string? workspacePath)
    {
        lock (gate)
        {
            WorkspaceHistoryDocument document = ReadDocument();
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                WriteDocument(document with { CurrentProject = null });
                return;
            }

            string normalized = NormalizePath(workspacePath);
            WorkspaceDescriptor descriptor = CreateDescriptor(
                normalized,
                DateTimeOffset.UtcNow,
                document.CurrentProject?.LastSuccessfullyLaunchedAtUtc);
            WriteDocument(UpdateCurrent(document, descriptor));
        }
    }

    public void ObserveWorkspace(WorkspaceDescriptor workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        lock (gate)
        {
            WorkspaceHistoryDocument document = ReadDocument();
            WorkspaceDescriptor normalized = NormalizeDescriptor(workspace);
            if (normalized.IsEmpty)
            {
                // Empty states are useful diagnostics, but a short-lived startup window must not
                // erase the last valid project. An explicit empty launch uses SaveLastWorkspace(null).
                return;
            }

            WriteDocument(UpdateCurrent(document, normalized));
        }
    }

    public void MarkLaunched(string workspacePath, DateTimeOffset launchedAtUtc)
    {
        string normalizedPath = NormalizePath(workspacePath);
        lock (gate)
        {
            WorkspaceHistoryDocument document = ReadDocument();
            WorkspaceDescriptor descriptor = document.CurrentProject is { } current
                && SamePath(current.Path, normalizedPath)
                    ? current with
                    {
                        LastSuccessfullyLaunchedAtUtc = launchedAtUtc,
                        PathExists = WorkspaceExists(current),
                    }
                    : CreateDescriptor(normalizedPath, launchedAtUtc, launchedAtUtc);
            WriteDocument(UpdateCurrent(document, descriptor));
        }
    }

    public void RemoveRecent(string workspacePath)
    {
        string normalized = NormalizePath(workspacePath);
        lock (gate)
        {
            WorkspaceHistoryDocument document = ReadDocument();
            WriteDocument(document with
            {
                RecentProjects = document.RecentProjects
                    .Where(item => !SamePath(item.Path, normalized))
                    .ToArray(),
            });
        }
    }

    public void ClearRecent()
    {
        lock (gate)
        {
            WorkspaceHistoryDocument document = ReadDocument();
            WriteDocument(document with { RecentProjects = [] });
        }
    }

    private WorkspaceHistoryDocument ReadDocument()
    {
        if (!File.Exists(filePath))
        {
            return WorkspaceHistoryDocument.Empty;
        }

        protectedPaths.AssertCanRead(filePath);
        try
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 1024 * 1024)
            {
                return WorkspaceHistoryDocument.Empty;
            }

            using JsonDocument json = JsonDocument.Parse(stream);
            if (json.RootElement.TryGetProperty("workspacePath", out JsonElement legacyPath))
            {
                string? path = legacyPath.GetString();
                if (string.IsNullOrWhiteSpace(path))
                {
                    return WorkspaceHistoryDocument.Empty;
                }

                WorkspaceDescriptor legacy = CreateDescriptor(path, DateTimeOffset.UtcNow, null);
                return new WorkspaceHistoryDocument(legacy, [legacy]);
            }

            WorkspaceHistoryDocument? document = json.RootElement.Deserialize<WorkspaceHistoryDocument>(SerializerOptions);
            if (document is null)
            {
                return WorkspaceHistoryDocument.Empty;
            }

            WorkspaceDescriptor? current = TryNormalize(document.CurrentProject);
            WorkspaceDescriptor[] recent = document.RecentProjects
                .Select(TryNormalize)
                .Where(static item => item is not null && !item.IsEmpty)
                .Select(static item => item!)
                .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderByDescending(static item => item.LastSuccessfullyObservedAtUtc)
                .Take(MaximumRecentProjects)
                .ToArray();
            return new WorkspaceHistoryDocument(current, recent);
        }
        catch (Exception exception) when (
            exception is JsonException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return WorkspaceHistoryDocument.Empty;
        }
    }

    private void WriteDocument(WorkspaceHistoryDocument document)
        => AtomicJsonFile.Write(filePath, document, SerializerOptions, protectedPaths);

    private WorkspaceHistoryDocument UpdateCurrent(
        WorkspaceHistoryDocument document,
        WorkspaceDescriptor descriptor)
    {
        WorkspaceDescriptor[] recent = document.RecentProjects
            .Where(item => !SamePath(item.Path, descriptor.Path))
            .Prepend(descriptor)
            .Take(MaximumRecentProjects)
            .ToArray();
        return new WorkspaceHistoryDocument(descriptor, recent);
    }

    private WorkspaceDescriptor? TryNormalize(WorkspaceDescriptor? descriptor)
    {
        try
        {
            return descriptor is null ? null : NormalizeDescriptor(descriptor);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private WorkspaceDescriptor NormalizeDescriptor(WorkspaceDescriptor descriptor)
    {
        if (descriptor.Type == WorkspaceType.Empty)
        {
            return descriptor with
            {
                DisplayName = string.Empty,
                Path = null,
                FolderPaths = [],
                PathExists = false,
            };
        }

        string path = NormalizePath(descriptor.Path ?? string.Empty);
        string[] folders = descriptor.FolderPaths
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();
        WorkspaceType type = descriptor.Type;
        if (type == WorkspaceType.Folder && !Directory.Exists(path))
        {
            type = Path.GetExtension(path).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase)
                ? WorkspaceType.WorkspaceFile
                : WorkspaceType.Folder;
        }

        if (type == WorkspaceType.WorkspaceFile
            && !Path.GetExtension(path).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Workspace files must use the .code-workspace extension.");
        }

        return descriptor with
        {
            Type = type,
            DisplayName = GetDisplayName(path),
            Path = path,
            FolderPaths = folders,
            PathExists = WorkspaceExists(type, path, folders),
        };
    }

    private WorkspaceDescriptor CreateDescriptor(
        string workspacePath,
        DateTimeOffset observedAtUtc,
        DateTimeOffset? launchedAtUtc)
    {
        string path = NormalizePath(workspacePath);
        WorkspaceType type = Path.GetExtension(path).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase)
            ? WorkspaceType.WorkspaceFile
            : WorkspaceType.Folder;
        return new WorkspaceDescriptor(
            type,
            GetDisplayName(path),
            path,
            type == WorkspaceType.Folder ? [path] : [],
            observedAtUtc,
            launchedAtUtc,
            WorkspaceExists(type, path, type == WorkspaceType.Folder ? [path] : []));
    }

    private string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || ContainsTraversal(path))
        {
            throw new ArgumentException("Workspace path is empty or contains traversal.", nameof(path));
        }

        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        if (!Path.IsPathFullyQualified(normalized))
        {
            throw new ArgumentException("Workspace path must be fully qualified.", nameof(path));
        }

        protectedPaths.AssertCanRead(normalized);
        return normalized;
    }

    private static bool ContainsTraversal(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment == "..");

    private static bool WorkspaceExists(WorkspaceDescriptor descriptor)
        => descriptor.Path is not null
            && WorkspaceExists(descriptor.Type, descriptor.Path, descriptor.FolderPaths);

    private static bool WorkspaceExists(
        WorkspaceType type,
        string path,
        IReadOnlyList<string> folders)
        => type switch
        {
            WorkspaceType.Folder => Directory.Exists(path),
            WorkspaceType.WorkspaceFile => File.Exists(path),
            WorkspaceType.MultiRoot => File.Exists(path) || folders.Any(Directory.Exists),
            _ => false,
        };

    private static string GetDisplayName(string path)
    {
        string name = Path.GetFileName(path);
        if (Path.GetExtension(name).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return string.IsNullOrWhiteSpace(name)
            ? new DirectoryInfo(path).Name
            : name;
    }

    private static bool SamePath(string? left, string? right)
        => left is not null
            && right is not null
            && Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private sealed record WorkspaceHistoryDocument(
        WorkspaceDescriptor? CurrentProject,
        IReadOnlyList<WorkspaceDescriptor> RecentProjects)
    {
        public static WorkspaceHistoryDocument Empty { get; } = new(null, []);
    }
}
