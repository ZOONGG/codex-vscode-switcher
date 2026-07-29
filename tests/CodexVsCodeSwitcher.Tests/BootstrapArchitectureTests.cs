using CodexVsCodeSwitcher.Core;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class BootstrapArchitectureTests
{
    [Fact]
    public void ProductIdentity_IsUnique()
    {
        Assert.Equal("CodexVsCodeSwitcher.exe", ProductIdentity.ExecutableName);
        Assert.Equal("ZOONGG.CodexVsCodeSwitcher", ProductIdentity.AppUserModelId);
        Assert.DoesNotContain("ProfileOverlay", ProductIdentity.MutexName, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("{00000000-0000-0000-0000-000000000000}", ProductIdentity.InstallerAppId);
        Assert.False(UpdateChannelConfiguration.Unconfigured.IsConfigured);
        Assert.Null(UpdateChannelConfiguration.Unconfigured.Endpoint);
    }

    [Fact]
    public void IntegrationContracts_ArePresentWithoutConcreteBackend()
    {
        Type[] contracts =
        [
            typeof(IVsCodeLocator),
            typeof(IVsCodeLauncher),
            typeof(IVsCodeProcessService),
            typeof(IVsCodeWindowLocator),
            typeof(ICodexProfileHomeService),
            typeof(IWorkspaceHistoryService),
            typeof(IProtectedPathPolicy),
        ];

        Assert.All(contracts, static contract => Assert.True(contract.IsInterface));
        Assert.False(VsCodeIntegrationStatus.Bootstrap.IsImplemented);
        Assert.True(VsCodeIntegrationStatus.Bootstrap.IsPrepared);
    }
}
