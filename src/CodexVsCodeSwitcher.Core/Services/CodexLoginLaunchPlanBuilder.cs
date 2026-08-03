using System.Text;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed record CodexLoginLaunchPlan(
    string PowerShellExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public sealed class CodexLoginLaunchPlanBuilder
{
    public CodexLoginLaunchPlan Build(
        string powerShellExecutablePath,
        string codexExecutablePath,
        string profileDirectory)
    {
        string powerShell = RequireFullPath(powerShellExecutablePath, nameof(powerShellExecutablePath));
        string codex = RequireFullPath(codexExecutablePath, nameof(codexExecutablePath));
        string profile = RequireFullPath(profileDirectory, nameof(profileDirectory));

        string profileBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(profile));
        string codexBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(codex));
        string command = string.Concat(
            "$ErrorActionPreference='Stop';",
            "$env:CODEX_HOME=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('",
            profileBase64,
            "'));",
            "$codex=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('",
            codexBase64,
            "'));",
            "& $codex login;",
            "exit $LASTEXITCODE");
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        return new CodexLoginLaunchPlan(
            powerShell,
            ["-NoLogo", "-NoProfile", "-EncodedCommand", encodedCommand],
            profile);
    }

    public static string GetWindowsPowerShellPath()
    {
        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            throw new PlatformNotSupportedException("The Windows directory could not be resolved.");
        }

        return Path.Combine(
            windowsDirectory,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }

    private static string RequireFullPath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Path cannot be empty.", parameterName);
        }

        string trimmed = value.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException("Path must be fully qualified.", parameterName);
        }

        return Path.GetFullPath(trimmed);
    }
}
