using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class VsCodeExecutableLocatorTests
{
    [Fact]
    public void FindsCustomVsCodeInstallationThroughPathCliShim()
    {
        using var temp = new TempDirectory();
        string installation = Path.Combine(temp.Path, "custom-vscode");
        string bin = Path.Combine(installation, "bin");
        Directory.CreateDirectory(bin);
        string executable = Path.Combine(installation, "Code.exe");
        File.WriteAllText(executable, "fake");
        File.WriteAllText(Path.Combine(bin, "code.cmd"), "fake");
        var locator = new VsCodeExecutableLocator("", "", "", bin);

        string? detected = locator.Locate(null);

        Assert.Equal(executable, detected);
    }

    [Fact]
    public void FindsExecutableDirectlyOnPath()
    {
        using var temp = new TempDirectory();
        string executable = Path.Combine(temp.Path, "Code.exe");
        File.WriteAllText(executable, "fake");
        var locator = new VsCodeExecutableLocator("", "", "", temp.Path);

        string? detected = locator.Locate(null);

        Assert.Equal(executable, detected);
    }

    [Fact]
    public void IgnoresUnrelatedCommandShims()
    {
        using var temp = new TempDirectory();
        string bin = Path.Combine(temp.Path, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(temp.Path, "Code.exe"), "fake");
        File.WriteAllText(Path.Combine(bin, "not-code.cmd"), "fake");
        var locator = new VsCodeExecutableLocator("", "", "", bin);

        Assert.Null(locator.Locate(null));
    }

    [Fact]
    public void ExplicitInvalidPathDoesNotFallBackToAutomaticDiscovery()
    {
        using var temp = new TempDirectory();
        string executable = Path.Combine(temp.Path, "Code.exe");
        File.WriteAllText(executable, "fake");
        var locator = new VsCodeExecutableLocator("", "", "", temp.Path);

        Assert.Null(locator.Locate(Path.Combine(temp.Path, "missing.exe")));
    }
}
