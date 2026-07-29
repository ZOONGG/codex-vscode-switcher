using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class VsCodeLaunchPlanBuilder
{
    public const string CodexHomeVariable = "CODEX_HOME";

    public VsCodeProcessStartSpec Build(
        string executablePath,
        string userDataDirectory,
        string extensionsDirectory,
        string profileCodexHome,
        string? workspacePath)
    {
        string executable = RequireFullPath(executablePath, nameof(executablePath));
        string userData = RequireFullPath(userDataDirectory, nameof(userDataDirectory));
        string extensions = RequireFullPath(extensionsDirectory, nameof(extensionsDirectory));
        string codexHome = RequireFullPath(profileCodexHome, nameof(profileCodexHome));
        string? workspace = string.IsNullOrWhiteSpace(workspacePath)
            ? null
            : RequireFullPath(workspacePath, nameof(workspacePath));

        var arguments = new List<string>
        {
            "--user-data-dir",
            userData,
            "--extensions-dir",
            extensions,
            "--new-window",
        };
        if (workspace is not null)
        {
            arguments.Add(workspace);
        }

        return new VsCodeProcessStartSpec(
            executable,
            arguments,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CodexHomeVariable] = codexHome,
            });
    }

    public VsCodeProcessStartSpec BuildExtensionInstall(
        string executablePath,
        string userDataDirectory,
        string extensionsDirectory)
    {
        return new VsCodeProcessStartSpec(
            RequireFullPath(executablePath, nameof(executablePath)),
            [
                "--user-data-dir",
                RequireFullPath(userDataDirectory, nameof(userDataDirectory)),
                "--extensions-dir",
                RequireFullPath(extensionsDirectory, nameof(extensionsDirectory)),
                "--install-extension",
                CodexExtensionManager.ExtensionId,
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
}
