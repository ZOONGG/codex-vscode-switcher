using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class VsCodeLaunchPlanBuilder
{
    public const string CodexHomeVariable = "CODEX_HOME";

    public VsCodeProcessStartSpec Build(
        string executablePath,
        string userDataDirectory,
        string extensionsDirectory,
        string sharedDataDirectory,
        string profileCodexHome,
        string? workspacePath,
        VsCodeExtensionMode extensionMode = VsCodeExtensionMode.Isolated,
        CustomCaEnvironmentVariable customCaVariable = CustomCaEnvironmentVariable.None,
        string? customCaCertificatePath = null)
    {
        string executable = RequireFullPath(executablePath, nameof(executablePath));
        string userData = RequireFullPath(userDataDirectory, nameof(userDataDirectory));
        string extensions = RequireFullPath(extensionsDirectory, nameof(extensionsDirectory));
        string sharedData = RequireFullPath(sharedDataDirectory, nameof(sharedDataDirectory));
        string codexHome = RequireFullPath(profileCodexHome, nameof(profileCodexHome));
        string? workspace = string.IsNullOrWhiteSpace(workspacePath)
            ? null
            : RequireFullPath(workspacePath, nameof(workspacePath));

        var arguments = new List<string>
        {
            "--user-data-dir",
            userData,
            "--shared-data-dir",
            sharedData,
            "--new-window",
        };
        if (extensionMode == VsCodeExtensionMode.Isolated)
        {
            arguments.Insert(2, "--extensions-dir");
            arguments.Insert(3, extensions);
        }
        if (workspace is not null)
        {
            arguments.Add(workspace);
        }

        return new VsCodeProcessStartSpec(
            executable,
            arguments,
            ManagedEnvironmentOverridesBuilder.Build(
                codexHome,
                customCaVariable,
                customCaCertificatePath));
    }

    public VsCodeProcessStartSpec BuildExtensionInstall(
        string executablePath,
        string userDataDirectory,
        string extensionsDirectory,
        string sharedDataDirectory)
        => BuildExtensionInstall(
            executablePath,
            userDataDirectory,
            extensionsDirectory,
            sharedDataDirectory,
            CodexExtensionManager.ExtensionId);

    public VsCodeProcessStartSpec BuildExtensionInstall(
        string executablePath,
        string userDataDirectory,
        string extensionsDirectory,
        string sharedDataDirectory,
        string extensionId)
    {
        string normalizedExtensionId = RequireExtensionId(extensionId);
        return new VsCodeProcessStartSpec(
            RequireFullPath(executablePath, nameof(executablePath)),
            [
                "--user-data-dir",
                RequireFullPath(userDataDirectory, nameof(userDataDirectory)),
                "--extensions-dir",
                RequireFullPath(extensionsDirectory, nameof(extensionsDirectory)),
                "--shared-data-dir",
                RequireFullPath(sharedDataDirectory, nameof(sharedDataDirectory)),
                "--install-extension",
                normalizedExtensionId,
            ],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string RequireFullPath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Path cannot be empty.", parameterName);
        }

        string fullPath = Path.GetFullPath(value.Trim());
        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException("Path must be fully qualified.", parameterName);
        }

        return fullPath;
    }

    private static string RequireExtensionId(string extensionId)
    {
        string value = extensionId?.Trim() ?? string.Empty;
        int separator = value.IndexOf('.');
        if (separator <= 0
            || separator == value.Length - 1
            || value.IndexOf('.', separator + 1) >= 0
            || value.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("The VS Code extension identifier is invalid.", nameof(extensionId));
        }

        return value;
    }
}
