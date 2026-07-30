using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ActivationDiagnosticsTests
{
    [Fact]
    public void Formatter_EmitsOnlyTheExactSanitizedAllowlist()
    {
        var diagnostic = new ActivationDiagnostic(
            ActivationFailureCategory.WindowDetectionTimeout,
            new DateTimeOffset(2026, 7, 30, 6, 7, 8, TimeSpan.Zero),
            "2.0.0-test",
            @"D:\Apps\Microsoft VS Code\Code.exe",
            @"C:\Temp\Switcher\Data",
            @"C:\Temp\Switcher\Extensions",
            "alpha",
            @"D:\Work\sample.code-workspace",
            [new ManagedProcessDiagnostic(42, DateTimeOffset.UnixEpoch)],
            "window-detection",
            "TimeoutException",
            "access_token=secret-value for person@example.test");

        string formatted = ActivationDiagnosticsFormatter.Format(diagnostic);

        Assert.Equal(
            """
            errorCategory: WindowDetectionTimeout
            timestampUtc: 2026-07-30T06:07:08.0000000+00:00
            appVersion: 2.0.0-test
            vsCodeExecutablePath: D:\Apps\Microsoft VS Code\Code.exe
            dedicatedUserDataDirectory: C:\Temp\Switcher\Data
            dedicatedExtensionsDirectory: C:\Temp\Switcher\Extensions
            profileId: alpha
            workspacePath: D:\Work\sample.code-workspace
            process: 42@1970-01-01T00:00:00.0000000+00:00
            timeoutStage: window-detection
            exceptionType: TimeoutException
            message: access_token=<redacted> for <redacted-email>
            """,
            formatted);
        Assert.DoesNotContain("secret-value", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("person@", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("environment", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeReset_ClearsOnlyManagedMetadata()
    {
        using var layout = new TestLayout();
        Directory.CreateDirectory(layout.Paths.ApplicationDataDirectory);
        Directory.CreateDirectory(layout.Paths.VsCodeUserDataDirectory);
        Directory.CreateDirectory(layout.Paths.VsCodeExtensionsDirectory);
        layout.ActiveProfileStore.Write("alpha");
        string dedicatedSetting = Path.Combine(layout.Paths.VsCodeUserDataDirectory, "settings.json");
        File.WriteAllText(dedicatedSetting, "{}");
        var store = new ManagedInstanceStore(
            layout.Paths.ManagedInstanceMetadataFile,
            layout.ProtectedPaths);
        store.Write(new ManagedVsCodeInstanceState(
            42,
            DateTimeOffset.UtcNow,
            "alpha",
            null,
            Path.Combine(layout.Paths.ApplicationDataDirectory, "Code.exe"),
            layout.Paths.VsCodeUserDataDirectory,
            layout.Paths.VsCodeExtensionsDirectory,
            99,
            DateTimeOffset.UtcNow));

        new ManagedRuntimeStateResetService(store).Reset();

        Assert.Null(store.Read());
        Assert.Equal("alpha", layout.ActiveProfileStore.Read());
        Assert.True(File.Exists(dedicatedSetting));
        Assert.True(Directory.Exists(layout.Paths.VsCodeExtensionsDirectory));
    }
}
