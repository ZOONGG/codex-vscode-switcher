using System.Text.Json;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed record CodexExtensionInstallationInfo(
    string ExtensionPath,
    string? Version,
    string? BackendExecutablePath);

public sealed class CodexExtensionInstallationLocator
{
    private readonly IProtectedPathPolicy protectedPaths;

    public CodexExtensionInstallationLocator(IProtectedPathPolicy protectedPaths)
        => this.protectedPaths = protectedPaths;

    public CodexExtensionInstallationInfo? Locate(string extensionsDirectory)
    {
        string root = Path.GetFullPath(extensionsDirectory);
        protectedPaths.AssertCanRead(root);
        if (!Directory.Exists(root))
        {
            return null;
        }

        foreach (string extensionPath in Directory
            .EnumerateDirectories(root, CodexExtensionManager.ExtensionId + "-*", SearchOption.TopDirectoryOnly)
            .Where(static path => !IsReparsePoint(path))
            .OrderByDescending(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            string backend = Path.Combine(
                extensionPath,
                "bin",
                "windows-x86_64",
                "codex.exe");
            return new CodexExtensionInstallationInfo(
                extensionPath,
                ReadVersion(Path.Combine(extensionPath, "package.json")),
                File.Exists(backend) && !IsReparsePoint(backend) ? backend : null);
        }

        return null;
    }

    private static string? ReadVersion(string packageFile)
    {
        try
        {
            using FileStream stream = File.OpenRead(packageFile);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
