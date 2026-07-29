namespace CodexVsCodeSwitcher.Core.Models;

public sealed class OverlaySettings
{
    public OverlayDisplayMode DisplayMode { get; set; } = OverlayDisplayMode.Auto;

    public PositionPreset PositionPreset { get; set; } = PositionPreset.AfterMenu;

    public double OffsetX { get; set; } = 396;

    public double OffsetY { get; set; } = 2;

    public double? FloatingLeft { get; set; }

    public double? FloatingTop { get; set; }

    public string FloatingMonitorId { get; set; } = string.Empty;

    public double Scale { get; set; } = 1;

    public bool AnimationsEnabled { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool ShowOverlayOnStart { get; set; } = true;

    public bool AutomaticallyDetectVsCode { get; set; } = true;

    public string CustomVsCodeExecutablePath { get; set; } = string.Empty;

    public string DedicatedVsCodeUserDataDirectory { get; set; } = string.Empty;

    public string DedicatedVsCodeExtensionsDirectory { get; set; } = string.Empty;

    public string CodexProfileRoot { get; set; } = string.Empty;

    public string LastOpenedWorkspace { get; set; } = string.Empty;

    public bool ReopenLastWorkspaceAfterSwitch { get; set; } = true;

    public bool AttachOverlayToVsCode { get; set; }

    public HotkeySettings Hotkeys { get; set; } = HotkeySettings.CreateDefault();

    public LanguagePreference Language { get; set; } = LanguagePreference.SystemDefault;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public double SettingsWindowLeft { get; set; } = -1;

    public double SettingsWindowTop { get; set; } = -1;

    public double SettingsWindowWidth { get; set; } = 1000;

    public double SettingsWindowHeight { get; set; } = 720;

    public bool ShowAutomaticLimitIndicators { get; set; } = false;

    public int GreenThresholdPercent { get; set; } = 60;

    public int YellowThresholdPercent { get; set; } = 25;

    public int StaleDataThresholdMinutes { get; set; } = 90;

    public int LowWarningThresholdPercent { get; set; } = 20;

    public bool ShowIndicatorsInOverlay { get; set; } = true;

    public bool ShowManualProfileEmojiInOverlay { get; set; }

    public bool WarnWhenNearlyExhausted { get; set; } = true;

    public int ActiveProfileRefreshIntervalMinutes { get; set; } = 15;

    public int InactiveProfileRefreshIntervalMinutes { get; set; } = 60;
}
