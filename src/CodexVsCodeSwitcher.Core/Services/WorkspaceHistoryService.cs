using System.Text.Json;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class WorkspaceHistoryService : IWorkspaceHistoryService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string filePath;
    private readonly IProtectedPathPolicy protectedPaths;

    public WorkspaceHistoryService(string filePath, IProtectedPathPolicy protectedPaths)
    {
        this.filePath = Path.GetFullPath(filePath);
        this.protectedPaths = protectedPaths;
        protectedPaths.AssertCanWrite(this.filePath);
    }

    public string? ReadLastWorkspace()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        protectedPaths.AssertCanRead(filePath);
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            WorkspaceHistoryDocument? document =
                JsonSerializer.Deserialize<WorkspaceHistoryDocument>(stream, SerializerOptions);
            return string.IsNullOrWhiteSpace(document?.WorkspacePath)
                ? null
                : Path.GetFullPath(document.WorkspacePath);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void SaveLastWorkspace(string? workspacePath)
    {
        string? normalized = string.IsNullOrWhiteSpace(workspacePath)
            ? null
            : Path.GetFullPath(workspacePath);
        AtomicJsonFile.Write(
            filePath,
            new WorkspaceHistoryDocument(normalized, DateTimeOffset.UtcNow),
            SerializerOptions,
            protectedPaths);
    }

    private sealed record WorkspaceHistoryDocument(
        string? WorkspacePath,
        DateTimeOffset UpdatedAtUtc);
}
