namespace CodexVsCodeSwitcher.Core.Models;

public sealed record CodexVsCodeProfile(
    string Id,
    string DisplayName,
    string CodexHomeDirectory,
    HotkeyGesture? Hotkey,
    string? LastWorkspace,
    ProfileStatusMetadata? UsageStatus);
