using System.Text.Json;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class CodexExtensionManager : ICodexExtensionManager
{
    public const string ExtensionId = "openai.chatgpt";
    private const string ExtensionDirectoryPrefix = ExtensionId + "-";
    private static readonly JsonSerializerOptions SettingsSerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly VsCodeLaunchPlanBuilder launchPlanBuilder;
    private readonly IProcessCommandRunner processRunner;
    private readonly IProtectedPathPolicy protectedPaths;

    public CodexExtensionManager(
        VsCodeLaunchPlanBuilder launchPlanBuilder,
        IProcessCommandRunner processRunner,
        IProtectedPathPolicy protectedPaths)
    {
        this.launchPlanBuilder = launchPlanBuilder;
        this.processRunner = processRunner;
        this.protectedPaths = protectedPaths;
    }

    public CodexExtensionStatus Detect(string extensionsDirectory)
    {
        string root = Path.GetFullPath(extensionsDirectory);
        protectedPaths.AssertCanRead(root);
        if (!Directory.Exists(root))
        {
            return new CodexExtensionStatus(CodexExtensionState.Missing);
        }

        foreach (string directory in Directory.EnumerateDirectories(root, ExtensionDirectoryPrefix + "*", SearchOption.TopDirectoryOnly))
        {
            if (IsReparsePoint(directory))
            {
                continue;
            }

            string? version = ReadVersion(Path.Combine(directory, "package.json"))
                ?? Path.GetFileName(directory)[ExtensionDirectoryPrefix.Length..].Split('-')[0];
            return new CodexExtensionStatus(CodexExtensionState.Installed, version);
        }

        return new CodexExtensionStatus(CodexExtensionState.Missing);
    }

    public async Task<CodexExtensionStatus> InstallAsync(
        string executablePath,
        string userDataDirectory,
        string extensionsDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
        {
            return new CodexExtensionStatus(
                CodexExtensionState.VsCodeCliUnavailable,
                DetailKey: "VsCodeCliUnavailable");
        }

        protectedPaths.AssertCanWrite(userDataDirectory);
        protectedPaths.AssertCanWrite(extensionsDirectory);
        Directory.CreateDirectory(userDataDirectory);
        Directory.CreateDirectory(extensionsDirectory);
        VsCodeProcessStartSpec plan =
            launchPlanBuilder.BuildExtensionInstall(executablePath, userDataDirectory, extensionsDirectory);
        int exitCode;
        try
        {
            exitCode = await processRunner.RunAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new CodexExtensionStatus(
                CodexExtensionState.InstallationFailed,
                DetailKey: "CodexExtensionInstallFailed");
        }

        CodexExtensionStatus detected = Detect(extensionsDirectory);
        return exitCode == 0 && detected.State == CodexExtensionState.Installed
            ? detected
            : new CodexExtensionStatus(
                CodexExtensionState.InstallationFailed,
                DetailKey: "CodexExtensionInstallFailed");
    }

    public void ConfigureDedicatedSettings(string userDataDirectory, bool openOnStartup)
    {
        string root = Path.GetFullPath(userDataDirectory);
        protectedPaths.AssertCanWrite(root);
        string userDirectory = Path.Combine(root, "User");
        string settingsFile = Path.Combine(userDirectory, "settings.json");
        protectedPaths.AssertCanWrite(settingsFile);
        Dictionary<string, JsonElement> settings = ReadExistingSettings(settingsFile);
        settings["chatgpt.openOnStartup"] = JsonSerializer.SerializeToElement(openOnStartup);
        AtomicJsonFile.Write(settingsFile, settings, SettingsSerializerOptions, protectedPaths);
    }

    private static Dictionary<string, JsonElement> ReadExistingSettings(string settingsFile)
    {
        if (!File.Exists(settingsFile))
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        try
        {
            using FileStream stream = File.OpenRead(settingsFile);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                stream,
                new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                }) ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Dedicated VS Code settings are not valid JSON.", exception);
        }
    }

    private static string? ReadVersion(string packageFile)
    {
        if (!File.Exists(packageFile) || IsReparsePoint(packageFile))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(packageFile);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
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
}
