using System.Text.Json;
using System.Text.Json.Serialization;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string settingsFile;
    private readonly CodexVsCodeStorageLayout? layout;
    private readonly IProtectedPathPolicy? protectedPaths;

    public SettingsService(string settingsFile)
    {
        this.settingsFile = Path.GetFullPath(settingsFile);
    }

    public SettingsService(CodexVsCodeStorageLayout layout, IProtectedPathPolicy protectedPaths)
    {
        this.layout = layout;
        this.protectedPaths = protectedPaths;
        settingsFile = Path.GetFullPath(layout.SettingsFile);
        protectedPaths.AssertCanWrite(settingsFile);
    }

    public OverlaySettings Load()
    {
        if (!File.Exists(settingsFile))
        {
            return new OverlaySettings();
        }

        try
        {
            using FileStream stream = File.OpenRead(settingsFile);
            return Normalize(JsonSerializer.Deserialize<OverlaySettings>(stream, SerializerOptions));
        }
        catch (JsonException)
        {
            return new OverlaySettings();
        }
        catch (IOException)
        {
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        protectedPaths?.AssertCanWrite(settingsFile);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);

        string temp = settingsFile + ".tmp";
        using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, SerializerOptions);
        }

        if (File.Exists(settingsFile))
        {
            File.Replace(temp, settingsFile, null);
        }
        else
        {
            File.Move(temp, settingsFile);
        }
    }

    private OverlaySettings Normalize(OverlaySettings? settings)
    {
        settings ??= new OverlaySettings();
        settings.OffsetX = ClampFinite(settings.OffsetX, 0, 4000, 396);
        settings.OffsetY = ClampFinite(settings.OffsetY, 0, 4000, 2);
        settings.Scale = ClampFinite(settings.Scale, 0.8, 1.4, 1);
        settings.SettingsWindowWidth = ClampFinite(settings.SettingsWindowWidth, 900, 1800, 1000);
        settings.SettingsWindowHeight = ClampFinite(settings.SettingsWindowHeight, 620, 1400, 720);
        settings.SettingsWindowLeft = NormalizeCoordinate(settings.SettingsWindowLeft, -1);
        settings.SettingsWindowTop = NormalizeCoordinate(settings.SettingsWindowTop, -1);
        settings.Hotkeys ??= HotkeySettings.CreateDefault();
        settings.Hotkeys.ProfileHotkeys ??= [];
        settings.YellowThresholdPercent = Math.Clamp(settings.YellowThresholdPercent, 1, 99);
        settings.GreenThresholdPercent = Math.Clamp(settings.GreenThresholdPercent, settings.YellowThresholdPercent + 1, 100);
        settings.StaleDataThresholdMinutes = Math.Clamp(settings.StaleDataThresholdMinutes, 10, 1440);
        settings.LowWarningThresholdPercent = Math.Clamp(settings.LowWarningThresholdPercent, 1, 99);
        settings.ActiveProfileRefreshIntervalMinutes = Math.Clamp(settings.ActiveProfileRefreshIntervalMinutes, 10, 1440);
        settings.InactiveProfileRefreshIntervalMinutes = Math.Clamp(settings.InactiveProfileRefreshIntervalMinutes, 10, 1440);
        if (layout is not null)
        {
            settings.DedicatedVsCodeUserDataDirectory = NormalizeConfiguredPath(
                settings.DedicatedVsCodeUserDataDirectory,
                layout.VsCodeUserDataDirectory);
            settings.DedicatedVsCodeExtensionsDirectory = NormalizeConfiguredPath(
                settings.DedicatedVsCodeExtensionsDirectory,
                layout.VsCodeExtensionsDirectory);
            settings.CodexProfileRoot = NormalizeConfiguredPath(settings.CodexProfileRoot, layout.ProfilesDirectory);
            settings.CustomVsCodeExecutablePath = NormalizeOptionalPath(settings.CustomVsCodeExecutablePath);
            settings.LastOpenedWorkspace = NormalizeOptionalPath(settings.LastOpenedWorkspace);

            protectedPaths?.AssertCanWrite(settings.DedicatedVsCodeUserDataDirectory);
            protectedPaths?.AssertCanWrite(settings.DedicatedVsCodeExtensionsDirectory);
            protectedPaths?.AssertCanWrite(settings.CodexProfileRoot);
        }

        return settings;
    }

    private static string NormalizeConfiguredPath(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : Path.GetFullPath(value.Trim());

    private static string NormalizeOptionalPath(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value.Trim());

    private static double NormalizeCoordinate(double value, double fallback)
    {
        return double.IsFinite(value) && Math.Abs(value) <= 100000
            ? value
            : fallback;
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
    {
        return double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }
}
