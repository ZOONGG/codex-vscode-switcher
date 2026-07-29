namespace CodexVsCodeSwitcher.Core.Models;

public sealed record VsCodeLaunchOptions(
    string ExecutablePath,
    string UserDataDirectory,
    string ExtensionsDirectory,
    string? WorkspacePath,
    string ProfileCodexHome,
    bool RequestNewWindow);
