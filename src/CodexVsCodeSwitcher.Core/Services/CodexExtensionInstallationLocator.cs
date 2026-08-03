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
            string? version = ReadOfficialVersion(Path.Combine(extensionPath, "package.json"));
            if (version is null)
            {
                continue;
            }

            string backend = Path.Combine(
                extensionPath,
                "bin",
                "windows-x86_64",
                "codex.exe");
            return new CodexExtensionInstallationInfo(
                extensionPath,
                version,
                File.Exists(backend) && !IsReparsePoint(backend) ? backend : null);
        }

        return null;
    }

    private static string? ReadOfficialVersion(string packageFile)
    {
        try
        {
            if (!File.Exists(packageFile) || IsReparsePoint(packageFile))
            {
                return null;
            }

            using FileStream stream = new(
                packageFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            if (stream.Length is <= 0 or > 1024 * 1024)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            string? publisher = ReadString(root, "publisher");
            string? name = ReadString(root, "name");
            string? version = ReadString(root, "version");
            return publisher?.Equals("openai", StringComparison.OrdinalIgnoreCase) == true
                && name?.Equals("chatgpt", StringComparison.OrdinalIgnoreCase) == true
                && version is { Length: > 0 and <= 64 }
                ? version
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
