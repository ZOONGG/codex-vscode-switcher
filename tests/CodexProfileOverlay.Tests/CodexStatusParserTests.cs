using System.Text.Json;
using CodexProfileOverlay.Core.Models;
using CodexProfileOverlay.Core.Services;

namespace CodexProfileOverlay.Tests;

public sealed class CodexStatusParserTests
{
    private static readonly TimeZoneInfo Omsk = TimeZoneInfo.CreateCustomTimeZone(
        "test-omsk",
        TimeSpan.FromHours(6),
        "test-omsk",
        "test-omsk");

    [Fact]
    public void StripTerminalControlSequences_RemovesAnsiAndBoxDrawing()
    {
        string raw = "\u001b[32m╭────╮\u001b[0m\r\n│ 5h limit: 99% left (resets 21:48) │";

        string sanitized = CodexStatusParser.StripTerminalControlSequences(raw);

        Assert.DoesNotContain("\u001b", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("╭", sanitized, StringComparison.Ordinal);
        Assert.Contains("5h limit: 99% left", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_SupportsFlexibleWhitespaceAndProgressBars()
    {
        UsageSnapshot snapshot = Parse("""
              5h   limit:   █████████░  99% left (resets 21:48)
            Weekly limit:   ▏          0% left (resets 14:04 on 9 Jul)
            """);

        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal(99, snapshot.Windows.Single(window => window.Name == "5h").RemainingPercent);
        Assert.Equal(0, snapshot.Windows.Single(window => window.Name == "Weekly").RemainingPercent);
    }

    [Fact]
    public void Parse_ConvertsResetTimeWithoutDateToWindowsLocalTime()
    {
        DateTimeOffset capturedAt = new(2026, 7, 5, 15, 0, 0, TimeSpan.Zero);

        UsageSnapshot? snapshot = CodexStatusParser.Parse(
            "5h limit: 99% left (resets 21:48)",
            capturedAt,
            "codex-cli 0.142.5",
            Omsk);

        DateTimeOffset reset = snapshot!.Windows.Single().ResetAt!.Value;
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 15, 48, 0, TimeSpan.Zero), reset);
    }

    [Fact]
    public void Parse_ConvertsResetTimeWithDateToWindowsLocalTime()
    {
        DateTimeOffset capturedAt = new(2026, 7, 5, 10, 0, 0, TimeSpan.Zero);

        UsageSnapshot snapshot = Parse("Weekly limit: 0% left (resets 14:04 on 9 Jul)", capturedAt);

        Assert.Equal(new DateTimeOffset(2026, 7, 9, 8, 4, 0, TimeSpan.Zero), snapshot.Windows.Single().ResetAt);
    }

    [Fact]
    public void Parse_UsesLowestWindowSoFreshShortWindowDoesNotHideWeeklyExhaustion()
    {
        UsageSnapshot snapshot = Parse("""
            5h limit:      99% left (resets 21:48)
            Weekly limit:   0% left (resets 14:04 on 9 Jul)
            """);

        Assert.Equal(0, UsageIntelligence.EffectiveRemainingPercent(snapshot));
        Assert.Equal("🔴", ProfileIndicatorFormatter.FormatAutomatic(snapshot, true, 60, 25, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(90), false));
    }

    [Fact]
    public void Parse_DiscardsPrivacyFieldsAndStoresOnlySafeSourceMetadata()
    {
        UsageSnapshot snapshot = Parse("""
            Account: user@example.invalid
            Session: 00000000-0000-0000-0000-000000000000
            Directory: C:\Users\person\secret-project
            Model: private-model
            5h limit: 75% left (resets 21:48)
            """);

        string json = JsonSerializer.Serialize(snapshot);
        Assert.Equal(CodexCliStatusUsageProvider.SourceIdentifier, snapshot.Source);
        Assert.Equal("codex-cli 0.142.5", snapshot.CodexCliVersion);
        Assert.DoesNotContain("user@example.invalid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-project", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-model", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MalformedOutputReturnsNull()
    {
        UsageSnapshot? snapshot = CodexStatusParser.Parse(
            "OpenAI usage is available on the web page.",
            DateTimeOffset.UtcNow,
            "codex-cli 0.142.5",
            Omsk);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task Provider_ReturnsNullForUnavailableTerminal()
    {
        var provider = new CodexCliStatusUsageProvider(new FakeTerminal(false, null), Omsk);

        UsageSnapshot? snapshot = await provider.GetUsageAsync(@"C:\fake-profile", CancellationToken.None);

        Assert.Equal(UsageProviderCapability.Unavailable, provider.Capability);
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task Provider_ParsesFakeTerminalOutput()
    {
        var provider = new CodexCliStatusUsageProvider(new FakeTerminal(true, """
            5h limit: 88% left (resets 21:48)
            Weekly limit: 61% left (resets 14:04 on 9 Jul)
            """), Omsk);

        UsageSnapshot? snapshot = await provider.GetUsageAsync(@"C:\fake-profile", CancellationToken.None);

        Assert.Equal(UsageProviderCapability.Supported, provider.Capability);
        Assert.Equal(61, UsageIntelligence.EffectiveRemainingPercent(snapshot!));
    }

    [Fact]
    public async Task SupportTest_ReturnsFalseOnTimeout()
    {
        using var temp = new TempDirectory();
        var service = new ProfileStatusService(
            new ProfileStatusStore(Path.Combine(temp.Path, "status.json")),
            new SlowProvider(),
            new SafeLogger(temp.Path));

        bool supported = await service.TestProviderSupportAsync(@"C:\fake-profile", CancellationToken.None, TimeSpan.FromMilliseconds(20));

        Assert.False(supported);
    }

    private static UsageSnapshot Parse(string value)
        => Parse(value, new DateTimeOffset(2026, 7, 5, 10, 0, 0, TimeSpan.Zero));

    private static UsageSnapshot Parse(string value, DateTimeOffset capturedAt)
        => CodexStatusParser.Parse(value, capturedAt, "codex-cli 0.142.5", Omsk)!;

    private sealed class FakeTerminal(bool available, string? output) : ICodexStatusTerminal
    {
        public bool IsAvailable => available;

        public Task<CodexStatusCapture?> CaptureStatusAsync(string profileDirectory, CancellationToken cancellationToken)
            => Task.FromResult(output is null
                ? null
                : new CodexStatusCapture(output, new DateTimeOffset(2026, 7, 5, 10, 0, 0, TimeSpan.Zero), "codex-cli 0.142.5"));
    }

    private sealed class SlowProvider : IUsageProvider
    {
        public UsageProviderCapability Capability => UsageProviderCapability.Supported;

        public async Task<UsageSnapshot?> GetUsageAsync(string profileDirectory, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return null;
        }
    }
}
