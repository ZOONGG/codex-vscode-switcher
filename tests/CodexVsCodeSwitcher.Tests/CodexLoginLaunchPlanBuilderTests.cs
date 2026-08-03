using System.Text;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class CodexLoginLaunchPlanBuilderTests
{
    [Fact]
    public void Build_EncodesUserControlledPathsInsteadOfInterpolatingPowerShell()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "switcher & ' profile"));
        string powerShell = Path.Combine(root, "powershell.exe");
        string codex = Path.Combine(root, "codex.exe");
        string profile = Path.Combine(root, "new % profile");

        CodexLoginLaunchPlan plan = new CodexLoginLaunchPlanBuilder().Build(
            powerShell,
            codex,
            profile);

        Assert.Equal(powerShell, plan.PowerShellExecutablePath);
        Assert.Equal(profile, plan.WorkingDirectory);
        Assert.Equal("-EncodedCommand", plan.Arguments[^2]);
        Assert.All(plan.Arguments, argument => Assert.DoesNotContain(profile, argument, StringComparison.Ordinal));
        Assert.All(plan.Arguments, argument => Assert.DoesNotContain(codex, argument, StringComparison.Ordinal));

        string command = Encoding.Unicode.GetString(Convert.FromBase64String(plan.Arguments[^1]));
        Assert.DoesNotContain(profile, command, StringComparison.Ordinal);
        Assert.DoesNotContain(codex, command, StringComparison.Ordinal);
        Assert.Contains("CODEX_HOME", command, StringComparison.Ordinal);
        Assert.Contains("login", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsRelativePaths()
    {
        var builder = new CodexLoginLaunchPlanBuilder();

        Assert.Throws<ArgumentException>(() => builder.Build("powershell.exe", "codex.exe", "profile"));
    }
}
