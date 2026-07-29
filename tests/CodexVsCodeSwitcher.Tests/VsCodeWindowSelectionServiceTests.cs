using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class VsCodeWindowSelectionServiceTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Microsoft VS Code\Code.exe")]
    [InlineData(@"C:\Program Files\Microsoft VS Code Insiders\Code - Insiders.exe")]
    public void Select_AcceptsSupportedVsCodeVariants(string executable)
    {
        var candidate = Candidate(executable, handle: 42);

        VsCodeWindowCandidate? selected = new VsCodeWindowSelectionService()
            .Select([candidate], customExecutablePath: null);

        Assert.Same(candidate, selected);
    }

    [Theory]
    [InlineData(@"C:\Program Files\ChatGPT\ChatGPT.exe")]
    [InlineData(@"C:\Program Files\Codex\Codex.exe")]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Tools\OtherElectron.exe")]
    public void Select_RejectsUnrelatedApplications(string executable)
    {
        VsCodeWindowCandidate? selected = new VsCodeWindowSelectionService()
            .Select([Candidate(executable, handle: 42)], customExecutablePath: null);

        Assert.Null(selected);
    }

    [Fact]
    public void Select_AcceptsOnlyExactConfiguredCustomExecutable()
    {
        const string configured = @"D:\PortableCode\MyCode.exe";
        VsCodeWindowCandidate[] candidates =
        [
            Candidate(@"D:\AnotherApp\MyCode.exe", handle: 12),
            Candidate(configured, handle: 44),
        ];

        VsCodeWindowCandidate? selected = new VsCodeWindowSelectionService()
            .Select(candidates, configured);

        Assert.Equal(44, selected?.Handle);
    }

    [Theory]
    [InlineData(false, true, 600, 400)]
    [InlineData(true, false, 600, 400)]
    [InlineData(true, true, 0, 400)]
    [InlineData(true, true, 600, 0)]
    public void Select_RejectsInvalidWindowHandles(bool visible, bool topLevel, int width, int height)
    {
        VsCodeWindowCandidate candidate = Candidate(
            @"C:\VSCode\Code.exe",
            handle: 42,
            visible,
            topLevel,
            width,
            height);

        Assert.Null(new VsCodeWindowSelectionService().Select([candidate], null));
    }

    private static VsCodeWindowCandidate Candidate(
        string executable,
        nint handle,
        bool visible = true,
        bool topLevel = true,
        int width = 1200,
        int height = 800)
        => new(123, executable, handle, visible, topLevel, width, height);
}
