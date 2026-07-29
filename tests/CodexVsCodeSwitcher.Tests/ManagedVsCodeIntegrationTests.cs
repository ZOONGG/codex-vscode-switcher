using System.Text.Json;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ManagedVsCodeIntegrationTests
{
    [Fact]
    public void LaunchPlan_ScopesCodexHomeToChildEnvironmentOnly()
    {
        using var temp = new TempDirectory();
        string before = Environment.GetEnvironmentVariable(VsCodeLaunchPlanBuilder.CodexHomeVariable) ?? string.Empty;

        VsCodeProcessStartSpec plan = BuildPlan(temp.Path, workspace: null);

        Assert.Equal(Path.Combine(temp.Path, "profile"), plan.EnvironmentOverrides["CODEX_HOME"]);
        Assert.Equal(before, Environment.GetEnvironmentVariable(VsCodeLaunchPlanBuilder.CodexHomeVariable) ?? string.Empty);
        Assert.Single(plan.EnvironmentOverrides);
    }

    [Fact]
    public void LaunchPlan_AlwaysUsesDedicatedArgumentsAndNewWindow()
    {
        using var temp = new TempDirectory();

        VsCodeProcessStartSpec plan = BuildPlan(temp.Path, workspace: null);

        AssertArgumentValue(plan.Arguments, "--user-data-dir", Path.Combine(temp.Path, "data"));
        AssertArgumentValue(plan.Arguments, "--extensions-dir", Path.Combine(temp.Path, "extensions"));
        Assert.Contains("--new-window", plan.Arguments);
    }

    [Fact]
    public void LaunchPlan_ReopensSelectedWorkspace()
    {
        using var temp = new TempDirectory();
        string workspace = Path.Combine(temp.Path, "repo");

        VsCodeProcessStartSpec plan = BuildPlan(temp.Path, workspace);

        Assert.Equal(Path.GetFullPath(workspace), plan.Arguments[^1]);
    }

    [Fact]
    public void LaunchPlan_CanOpenEmptyWindow()
    {
        using var temp = new TempDirectory();

        VsCodeProcessStartSpec plan = BuildPlan(temp.Path, workspace: null);

        Assert.Equal(5, plan.Arguments.Count);
        Assert.Equal("--new-window", plan.Arguments[^1]);
    }

    [Fact]
    public void ExtensionInstallPlan_UsesOnlyDedicatedExtensionDirectory()
    {
        using var temp = new TempDirectory();
        string ordinaryExtensions = Path.Combine(temp.Path, "ordinary");
        VsCodeProcessStartSpec plan = new VsCodeLaunchPlanBuilder().BuildExtensionInstall(
            Path.Combine(temp.Path, "Code.exe"),
            Path.Combine(temp.Path, "data"),
            Path.Combine(temp.Path, "dedicated"));

        AssertArgumentValue(plan.Arguments, "--extensions-dir", Path.Combine(temp.Path, "dedicated"));
        Assert.DoesNotContain(ordinaryExtensions, plan.Arguments);
        int installIndex = plan.Arguments.IndexOf("--install-extension");
        Assert.True(installIndex >= 0 && installIndex + 1 < plan.Arguments.Count);
        Assert.Equal(CodexExtensionManager.ExtensionId, plan.Arguments[installIndex + 1]);
        Assert.Empty(plan.EnvironmentOverrides);
    }

    [Fact]
    public void ManagedIdentity_RejectsCodeProcessWithoutDedicatedArguments()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            42,
            state.RootProcessStartTimeUtc,
            state.ExecutablePath,
            [state.ExecutablePath, "--new-window"]);

        Assert.False(new ManagedProcessIdentityPolicy().IsManagedRoot(state, evidence));
    }

    [Fact]
    public void ManagedIdentity_AcceptsExactExecutableArgumentsPidAndStartTime()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            42,
            state.RootProcessStartTimeUtc,
            state.ExecutablePath,
            [
                state.ExecutablePath,
                "--user-data-dir",
                state.UserDataDirectory,
                "--extensions-dir",
                state.ExtensionsDirectory,
                "--new-window",
            ]);

        Assert.True(new ManagedProcessIdentityPolicy().IsManagedRoot(state, evidence));
    }

    [Fact]
    public void ManagedIdentity_RejectsStalePidStartTime()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            42,
            state.RootProcessStartTimeUtc.AddMinutes(1),
            state.ExecutablePath,
            [
                "--user-data-dir",
                state.UserDataDirectory,
                "--extensions-dir",
                state.ExtensionsDirectory,
            ]);

        Assert.False(new ManagedProcessIdentityPolicy().IsManagedRoot(state, evidence));
    }

    [Fact]
    public void ManagedIdentity_RejectsDifferentExecutable()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            42,
            state.RootProcessStartTimeUtc,
            Path.Combine(temp.Path, "OtherCode.exe"),
            [
                "--user-data-dir",
                state.UserDataDirectory,
                "--extensions-dir",
                state.ExtensionsDirectory,
            ]);

        Assert.False(new ManagedProcessIdentityPolicy().IsManagedRoot(state, evidence));
    }

    [Fact]
    public void ManagedIdentity_AcceptsVerifiedLauncherHandoffCandidate()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            84,
            state.LaunchTimestampUtc.AddSeconds(1),
            state.ExecutablePath,
            [
                state.ExecutablePath,
                $"--user-data-dir={state.UserDataDirectory}",
                "--extensions-dir",
                state.ExtensionsDirectory,
                "--new-window",
            ]);

        Assert.True(new ManagedProcessIdentityPolicy().IsManagedRootHandoffCandidate(state, evidence));
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(70)]
    public void ManagedIdentity_RejectsLauncherHandoffOutsideLaunchWindow(int secondsFromLaunch)
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            84,
            state.LaunchTimestampUtc.AddSeconds(secondsFromLaunch),
            state.ExecutablePath,
            [
                "--user-data-dir",
                state.UserDataDirectory,
                "--extensions-dir",
                state.ExtensionsDirectory,
            ]);

        Assert.False(new ManagedProcessIdentityPolicy().IsManagedRootHandoffCandidate(state, evidence));
    }

    [Fact]
    public void ManagedIdentity_RejectsLauncherHandoffWithoutDedicatedArguments()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            84,
            state.LaunchTimestampUtc.AddSeconds(1),
            state.ExecutablePath,
            [state.ExecutablePath, "--new-window"]);

        Assert.False(new ManagedProcessIdentityPolicy().IsManagedRootHandoffCandidate(state, evidence));
    }

    [Fact]
    public async Task Activation_DoesNotCopyProfileOrAuthenticationFile()
    {
        using var context = new ActivationContext();
        string auth = Path.Combine(context.ProfileDirectory, "auth.json");
        DateTime writeTime = File.GetLastWriteTimeUtc(auth);
        string[] before = Directory.GetFileSystemEntries(context.ProfileDirectory, "*", SearchOption.AllDirectories);

        ProfileActivationResult result = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.Succeeded, result.Status);
        Assert.Equal("{\"fake\":true}", File.ReadAllText(auth));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(auth));
        Assert.Equal(before, Directory.GetFileSystemEntries(context.ProfileDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Activation_MarksProfileActiveOnlyAfterVerifiedWindow()
    {
        using var context = new ActivationContext();
        context.Runtime.WaitResults.Enqueue(null);

        ProfileActivationResult result = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.WindowNotFound, result.Status);
        Assert.Null(context.Layout.ActiveProfileStore.Read());
    }

    [Fact]
    public async Task Activation_PersistsSanitizedManagedMetadataAfterSuccess()
    {
        using var context = new ActivationContext();

        ProfileActivationResult result = await context.ActivateAsync("alpha");
        ManagedVsCodeInstanceState? state = context.InstanceStore.Read();

        Assert.Equal(ProfileActivationStatus.Succeeded, result.Status);
        Assert.NotNull(state);
        Assert.Equal("alpha", state.SelectedProfileId);
        Assert.NotEqual(0, state.LastVerifiedWindowHandle);
        string serialized = File.ReadAllText(context.Layout.Paths.ManagedInstanceMetadataFile);
        Assert.DoesNotContain("CODEX_HOME", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fake", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Activation_DoesNotShutdownOrdinaryVsCode()
    {
        using var context = new ActivationContext();
        context.Runtime.OrdinaryProcessId = 9999;

        await context.ActivateAsync("alpha");

        Assert.DoesNotContain(context.Runtime.OrdinaryProcessId, context.Runtime.ShutdownTargets);
        Assert.DoesNotContain(context.Runtime.OrdinaryProcessId, context.Runtime.ForceCloseTargets);
    }

    [Fact]
    public async Task BlockedGracefulShutdown_AbortsWithoutLaunching()
    {
        using var context = new ActivationContext();
        context.ConfigurePreviousManagedProfile("beta");
        context.Runtime.ShutdownStatus = ManagedShutdownStatus.Blocked;

        ProfileActivationResult result = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.ShutdownBlocked, result.Status);
        Assert.Empty(context.Runtime.LaunchPlans);
        Assert.Equal("beta", context.Layout.ActiveProfileStore.Read());
    }

    [Fact]
    public async Task ConcurrentSwitches_AreRejectedWhileFirstOwnsLock()
    {
        using var context = new ActivationContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Runtime.WaitHandler = async (state, cancellationToken) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return FakeRuntime.SuccessObservation(state);
        };

        Task<ProfileActivationResult> first = context.ActivateAsync("alpha");
        await entered.Task;
        ProfileActivationResult second = await context.ActivateAsync("alpha");
        release.SetResult();

        Assert.Equal(ProfileActivationStatus.Busy, second.Status);
        Assert.Equal(ProfileActivationStatus.Succeeded, (await first).Status);
    }

    [Fact]
    public async Task LaunchFailure_RollsBackPreviousProfileOnce()
    {
        using var context = new ActivationContext();
        context.ConfigurePreviousManagedProfile("beta");
        context.Runtime.WaitResults.Enqueue(null);
        context.Runtime.WaitResults.Enqueue(FakeRuntime.SuccessSentinel);

        ProfileActivationResult result = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.RolledBack, result.Status);
        Assert.True(result.RollbackAttempted);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(2, context.Runtime.LaunchPlans.Count);
        Assert.Equal("beta", context.Layout.ActiveProfileStore.Read());
    }

    [Fact]
    public async Task FailedLaunchNeverHighlightsNewProfile()
    {
        using var context = new ActivationContext();
        context.ConfigurePreviousManagedProfile("beta");
        context.Runtime.WaitResults.Enqueue(null);
        context.Runtime.WaitResults.Enqueue(null);

        ProfileActivationResult result = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.RollbackFailed, result.Status);
        Assert.NotEqual("alpha", context.Layout.ActiveProfileStore.Read());
    }

    [Fact]
    public async Task LiveFailedLaunch_BlocksUnsafeSimultaneousRollback()
    {
        using var context = new ActivationContext();
        context.ConfigurePreviousManagedProfile("beta");
        context.Runtime.WaitResults.Enqueue(null);
        context.Runtime.KeepLaunchedProcessAliveWhenWindowMissing = true;
        context.Runtime.ShutdownResults.Enqueue(ManagedShutdownStatus.Closed);
        context.Runtime.ShutdownResults.Enqueue(ManagedShutdownStatus.InvalidTarget);

        ProfileActivationResult result = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.WindowNotFound, result.Status);
        Assert.False(result.SafeToRollback);
        Assert.Single(context.Runtime.LaunchPlans);
        Assert.Equal("beta", context.Layout.ActiveProfileStore.Read());
    }

    [Fact]
    public void ForceCloseTargetsOnlyReverifiedManagedPids()
    {
        using var context = new ActivationContext();
        context.ConfigurePreviousManagedProfile("beta");

        context.Service.ForceCloseCurrent();

        Assert.Equal(new[] { 10, 11 }, context.Runtime.ForceCloseTargets.OrderBy(static value => value));
        Assert.DoesNotContain(context.Runtime.OrdinaryProcessId, context.Runtime.ForceCloseTargets);
    }

    [Fact]
    public async Task MissingExtension_BlocksActivationWithoutNetworkOrLaunch()
    {
        using var context = new ActivationContext();
        context.ExtensionManager.Status = new CodexExtensionStatus(CodexExtensionState.Missing);

        ProfileActivationResult result = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.ExtensionMissing, result.Status);
        Assert.Empty(context.Runtime.LaunchPlans);
        Assert.Equal(0, context.ExtensionManager.InstallCalls);
    }

    [Fact]
    public async Task MissingWorkspace_IsReportedWithoutErasingHistory()
    {
        using var context = new ActivationContext();
        string missing = Path.Combine(context.Root, "missing.code-workspace");
        context.WorkspaceHistory.SaveLastWorkspace(missing);

        ProfileActivationResult result = await context.ActivateAsync("alpha", missing);

        Assert.Equal(ProfileActivationStatus.WorkspaceMissing, result.Status);
        Assert.Equal(missing, context.WorkspaceHistory.ReadLastWorkspace());
    }

    [Fact]
    public void ExtensionDetection_FindsOfficialExtensionVersion()
    {
        using var layout = new TestLayout();
        string extension = Path.Combine(layout.Paths.VsCodeExtensionsDirectory, "openai.chatgpt-1.2.3-win32-x64");
        Directory.CreateDirectory(extension);
        File.WriteAllText(Path.Combine(extension, "package.json"), "{\"version\":\"1.2.3\"}");
        var manager = new CodexExtensionManager(
            new VsCodeLaunchPlanBuilder(),
            new NoopProcessRunner(),
            layout.ProtectedPaths);

        CodexExtensionStatus status = manager.Detect(layout.Paths.VsCodeExtensionsDirectory);

        Assert.Equal(CodexExtensionState.Installed, status.State);
        Assert.Equal("1.2.3", status.Version);
    }

    [Fact]
    public void ExtensionDetection_ReportsMissingWithoutCreatingDirectories()
    {
        using var layout = new TestLayout();
        var manager = new CodexExtensionManager(
            new VsCodeLaunchPlanBuilder(),
            new NoopProcessRunner(),
            layout.ProtectedPaths);

        CodexExtensionStatus status = manager.Detect(layout.Paths.VsCodeExtensionsDirectory);

        Assert.Equal(CodexExtensionState.Missing, status.State);
        Assert.False(Directory.Exists(layout.Paths.VsCodeExtensionsDirectory));
    }

    [Fact]
    public void DedicatedSettings_PreserveUnrelatedValues()
    {
        using var layout = new TestLayout();
        string user = Path.Combine(layout.Paths.VsCodeUserDataDirectory, "User");
        Directory.CreateDirectory(user);
        string settings = Path.Combine(user, "settings.json");
        File.WriteAllText(settings, "{\"editor.fontSize\":17}");
        var manager = new CodexExtensionManager(
            new VsCodeLaunchPlanBuilder(),
            new NoopProcessRunner(),
            layout.ProtectedPaths);

        manager.ConfigureDedicatedSettings(layout.Paths.VsCodeUserDataDirectory, openOnStartup: true);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settings));
        Assert.Equal(17, document.RootElement.GetProperty("editor.fontSize").GetInt32());
        Assert.True(document.RootElement.GetProperty("chatgpt.openOnStartup").GetBoolean());
    }

    [Fact]
    public void WorkspaceHistory_RoundTripsFolderAndEmptyWindow()
    {
        using var layout = new TestLayout();
        var history = new WorkspaceHistoryService(layout.Paths.LastWorkspaceMetadataFile, layout.ProtectedPaths);
        string workspace = Path.Combine(layout.UserProfile, "repo");

        history.SaveLastWorkspace(workspace);
        Assert.Equal(Path.GetFullPath(workspace), history.ReadLastWorkspace());

        history.SaveLastWorkspace(null);
        Assert.Null(history.ReadLastWorkspace());
    }

    private static VsCodeProcessStartSpec BuildPlan(string root, string? workspace)
        => new VsCodeLaunchPlanBuilder().Build(
            Path.Combine(root, "Code.exe"),
            Path.Combine(root, "data"),
            Path.Combine(root, "extensions"),
            Path.Combine(root, "profile"),
            workspace);

    private static ManagedVsCodeInstanceState State(string root, int processId)
        => new(
            processId,
            DateTimeOffset.UtcNow,
            "alpha",
            null,
            Path.Combine(root, "Code.exe"),
            Path.Combine(root, "data"),
            Path.Combine(root, "extensions"),
            0,
            DateTimeOffset.UtcNow);

    private static void AssertArgumentValue(
        IReadOnlyList<string> arguments,
        string option,
        string expected)
    {
        int index = arguments.IndexOf(option);
        Assert.True(index >= 0 && index + 1 < arguments.Count);
        Assert.Equal(Path.GetFullPath(expected), arguments[index + 1]);
    }

    private sealed class ActivationContext : IDisposable
    {
        public ActivationContext()
        {
            Layout = new TestLayout();
            Root = Layout.Paths.ApplicationDataDirectory;
            Directory.CreateDirectory(Root);
            ProfileDirectory = Path.Combine(Layout.Paths.ProfilesDirectory, "alpha");
            Layout.AddProfile("alpha", "{\"fake\":true}");
            Layout.AddProfile("beta", "{\"fake\":true}");
            File.WriteAllText(ExecutablePath, string.Empty);
            Directory.CreateDirectory(Layout.Paths.VsCodeUserDataDirectory);
            Directory.CreateDirectory(Layout.Paths.VsCodeExtensionsDirectory);
            InstanceStore = new ManagedInstanceStore(Layout.Paths.ManagedInstanceMetadataFile, Layout.ProtectedPaths);
            WorkspaceHistory = new WorkspaceHistoryService(
                Layout.Paths.LastWorkspaceMetadataFile,
                Layout.ProtectedPaths);
            Runtime = new FakeRuntime();
            ExtensionManager = new FakeExtensionManager();
            Service = new ProfileActivationService(
                Layout.Paths.ProfilesDirectory,
                Layout.ProtectedPaths,
                new FixedLocator(ExecutablePath),
                Runtime,
                InstanceStore,
                Layout.ActiveProfileStore,
                WorkspaceHistory,
                ExtensionManager,
                new VsCodeLaunchPlanBuilder());
        }

        public TestLayout Layout { get; }
        public string Root { get; }
        public string ProfileDirectory { get; }
        public string ExecutablePath => Path.Combine(Root, "Code.exe");
        public ManagedInstanceStore InstanceStore { get; }
        public WorkspaceHistoryService WorkspaceHistory { get; }
        public FakeRuntime Runtime { get; }
        public FakeExtensionManager ExtensionManager { get; }
        public ProfileActivationService Service { get; }

        public Task<ProfileActivationResult> ActivateAsync(string profile, string? workspace = null)
            => Service.ActivateAsync(
                profile,
                ExecutablePath,
                Layout.Paths.VsCodeUserDataDirectory,
                Layout.Paths.VsCodeExtensionsDirectory,
                workspace,
                TimeSpan.FromMilliseconds(50),
                restartIfAlreadyActive: false,
                requireExtension: true,
                openCodexOnStartup: true);

        public void ConfigurePreviousManagedProfile(string profile)
        {
            ManagedVsCodeInstanceState state = new(
                10,
                DateTimeOffset.UtcNow,
                profile,
                null,
                ExecutablePath,
                Layout.Paths.VsCodeUserDataDirectory,
                Layout.Paths.VsCodeExtensionsDirectory,
                101,
                DateTimeOffset.UtcNow);
            InstanceStore.Write(state);
            Layout.ActiveProfileStore.Write(profile);
            Runtime.Current = new ManagedVsCodeObservation(
                state,
                new HashSet<int> { 10, 11 },
                new ManagedVsCodeWindow(101, 10, state.RootProcessStartTimeUtc, true, false));
        }

        public void Dispose() => Layout.Dispose();
    }

    private sealed class FixedLocator(string executablePath) : IVsCodeExecutableLocator
    {
        public string? Locate(string? configuredExecutablePath)
            => File.Exists(executablePath) ? executablePath : null;
    }

    private sealed class FakeExtensionManager : ICodexExtensionManager
    {
        public CodexExtensionStatus Status { get; set; } =
            new(CodexExtensionState.Installed, "test");

        public int InstallCalls { get; private set; }

        public CodexExtensionStatus Detect(string extensionsDirectory) => Status;

        public Task<CodexExtensionStatus> InstallAsync(
            string executablePath,
            string userDataDirectory,
            string extensionsDirectory,
            CancellationToken cancellationToken)
        {
            InstallCalls++;
            return Task.FromResult(Status);
        }

        public void ConfigureDedicatedSettings(string userDataDirectory, bool openOnStartup)
        {
        }
    }

    private sealed class FakeRuntime : IManagedVsCodeRuntime
    {
        public static readonly ManagedVsCodeObservation SuccessSentinel = new(
            new ManagedVsCodeInstanceState(
                1,
                DateTimeOffset.UnixEpoch,
                "alpha",
                null,
                Path.GetFullPath("Code.exe"),
                Path.GetFullPath("data"),
                Path.GetFullPath("extensions"),
                1,
                DateTimeOffset.UnixEpoch),
            new HashSet<int> { 1 },
            new ManagedVsCodeWindow(1, 1, DateTimeOffset.UnixEpoch, true, false));

        private int nextProcessId = 100;

        public ManagedVsCodeObservation? Current { get; set; }
        public ManagedShutdownStatus ShutdownStatus { get; set; } = ManagedShutdownStatus.Closed;
        public Queue<ManagedShutdownStatus> ShutdownResults { get; } = new();
        public List<VsCodeProcessStartSpec> LaunchPlans { get; } = [];
        public Queue<ManagedVsCodeObservation?> WaitResults { get; } = new();
        public Func<ManagedVsCodeInstanceState, CancellationToken, Task<ManagedVsCodeObservation?>>? WaitHandler { get; set; }
        public HashSet<int> ShutdownTargets { get; } = [];
        public HashSet<int> ForceCloseTargets { get; } = [];
        public int OrdinaryProcessId { get; set; } = 9999;
        public bool KeepLaunchedProcessAliveWhenWindowMissing { get; set; }

        public ManagedVsCodeObservation? Observe(ManagedVsCodeInstanceState state)
            => Current?.State.RootProcessId == state.RootProcessId ? Current : null;

        public Task<ManagedShutdownResult> RequestCloseAsync(
            ManagedVsCodeObservation instance,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            foreach (int processId in instance.VerifiedProcessIds)
            {
                ShutdownTargets.Add(processId);
            }

            ManagedShutdownStatus status = ShutdownResults.TryDequeue(out ManagedShutdownStatus queued)
                ? queued
                : ShutdownStatus;
            if (status == ManagedShutdownStatus.Closed)
            {
                Current = null;
            }

            return Task.FromResult(new ManagedShutdownResult(status, instance.VerifiedProcessIds));
        }

        public ManagedProcessIdentity Launch(VsCodeProcessStartSpec startSpec)
        {
            LaunchPlans.Add(startSpec);
            return new ManagedProcessIdentity(nextProcessId++, DateTimeOffset.UtcNow);
        }

        public async Task<ManagedVsCodeObservation?> WaitForWindowAsync(
            ManagedVsCodeInstanceState state,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ManagedVsCodeObservation? result;
            if (WaitHandler is not null)
            {
                result = await WaitHandler(state, cancellationToken);
            }
            else if (WaitResults.TryDequeue(out ManagedVsCodeObservation? queued))
            {
                result = ReferenceEquals(queued, SuccessSentinel) ? SuccessObservation(state) : queued;
            }
            else
            {
                result = SuccessObservation(state);
            }

            Current = result ?? (KeepLaunchedProcessAliveWhenWindowMissing
                ? new ManagedVsCodeObservation(
                    state,
                    new HashSet<int> { state.RootProcessId },
                    Window: null)
                : null);
            return result;
        }

        public void ForceClose(ManagedVsCodeObservation instance)
        {
            foreach (int processId in instance.VerifiedProcessIds)
            {
                ForceCloseTargets.Add(processId);
            }
        }

        public static ManagedVsCodeObservation SuccessObservation(ManagedVsCodeInstanceState state)
            => new(
                state,
                new HashSet<int> { state.RootProcessId },
                new ManagedVsCodeWindow(
                    500 + state.RootProcessId,
                    state.RootProcessId,
                    state.RootProcessStartTimeUtc,
                    true,
                    false));
    }

    private sealed class NoopProcessRunner : IProcessCommandRunner
    {
        public Task<int> RunAsync(VsCodeProcessStartSpec startSpec, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}

internal static class ReadOnlyListTestExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string expected)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index].Equals(expected, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
