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
    public void ProcessStartInfo_PreservesTheCompleteParentEnvironmentAndOverridesOnlyCodexHome()
    {
        using var temp = new TempDirectory();
        VsCodeProcessStartSpec plan = BuildPlan(temp.Path, workspace: null);

        System.Diagnostics.ProcessStartInfo startInfo = ManagedProcessStartInfoFactory.Create(plan);
        var inherited = Environment.GetEnvironmentVariables();

        foreach (System.Collections.DictionaryEntry variable in inherited)
        {
            string name = Assert.IsType<string>(variable.Key);
            string value = Assert.IsType<string>(variable.Value);
            if (name.Equals(VsCodeLaunchPlanBuilder.CodexHomeVariable, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.True(startInfo.Environment.TryGetValue(name, out string? actual));
            Assert.Equal(value, actual);
        }

        Assert.Equal(Path.Combine(temp.Path, "profile"), startInfo.Environment["CODEX_HOME"]);
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), startInfo.Environment["PATH"]);
    }

    [Theory]
    [InlineData("HTTP_PROXY")]
    [InlineData("HTTPS_PROXY")]
    [InlineData("ALL_PROXY")]
    [InlineData("NO_PROXY")]
    [InlineData("CODEX_CA_CERTIFICATE")]
    [InlineData("SSL_CERT_FILE")]
    [InlineData("SSL_CERT_DIR")]
    [InlineData("NODE_EXTRA_CA_CERTS")]
    [InlineData("REQUESTS_CA_BUNDLE")]
    [InlineData("CURL_CA_BUNDLE")]
    public void ProcessStartInfo_PreservesNetworkVariableWhenPresent(string variableName)
    {
        using var temp = new TempDirectory();
        string? inherited = Environment.GetEnvironmentVariable(variableName);
        System.Diagnostics.ProcessStartInfo startInfo =
            ManagedProcessStartInfoFactory.Create(BuildPlan(temp.Path, workspace: null));

        if (inherited is null)
        {
            Assert.False(startInfo.Environment.ContainsKey(variableName));
        }
        else
        {
            Assert.Equal(inherited, startInfo.Environment[variableName]);
        }
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
    public void SharedExtensionsLaunchPlan_OmitsExtensionsDirectoryArgument()
    {
        using var temp = new TempDirectory();

        VsCodeProcessStartSpec plan = BuildPlan(
            temp.Path,
            workspace: null,
            VsCodeExtensionMode.Shared);

        Assert.DoesNotContain("--extensions-dir", plan.Arguments);
        Assert.DoesNotContain(Path.Combine(temp.Path, "extensions"), plan.Arguments);
        AssertArgumentValue(plan.Arguments, "--user-data-dir", Path.Combine(temp.Path, "data"));
        Assert.DoesNotContain("--shared-data-dir", plan.Arguments);
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

        Assert.Equal(7, plan.Arguments.Count);
        AssertArgumentValue(
            plan.Arguments,
            "--shared-data-dir",
            Path.Combine(temp.Path, "shared-data"));
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
            Path.Combine(temp.Path, "dedicated"),
            Path.Combine(temp.Path, "shared-data"));

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
    public void SameProjectInOrdinaryVsCode_IsNotAdoptedAsManagedInstance()
    {
        using var temp = new TempDirectory();
        string project = Path.Combine(temp.Path, "same project");
        Directory.CreateDirectory(project);
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42) with
        {
            WorkspacePath = project,
            ExtensionMode = VsCodeExtensionMode.Shared,
        };
        var ordinary = new ProcessIdentityEvidence(
            84,
            state.RootProcessStartTimeUtc,
            state.ExecutablePath,
            [
                state.ExecutablePath,
                "--user-data-dir",
                Path.Combine(temp.Path, "ordinary-data"),
                "--new-window",
                project,
            ]);

        var policy = new ManagedProcessIdentityPolicy();
        Assert.False(policy.IsManagedRoot(state, ordinary));
        Assert.False(policy.IsExactManagedProcessCandidate(state, ordinary));
        Assert.Equal(project, state.WorkspacePath);
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
                "--shared-data-dir",
                state.SharedDataDirectory,
                "--new-window",
            ]);

        Assert.True(new ManagedProcessIdentityPolicy().IsManagedRoot(state, evidence));
    }

    [Fact]
    public void ManagedIdentity_AcceptsSharedExtensionsOnlyWithoutExtensionsArgument()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42) with
        {
            ExtensionMode = VsCodeExtensionMode.Shared,
        };
        var sharedEvidence = new ProcessIdentityEvidence(
            42,
            state.RootProcessStartTimeUtc,
            state.ExecutablePath,
            [
                state.ExecutablePath,
                "--user-data-dir",
                state.UserDataDirectory,
                "--new-window",
            ]);
        var isolatedEvidence = sharedEvidence with
        {
            CommandLineArguments =
            [
                .. sharedEvidence.CommandLineArguments,
                "--extensions-dir",
                state.ExtensionsDirectory,
            ],
        };

        var policy = new ManagedProcessIdentityPolicy();
        Assert.True(policy.IsManagedRoot(state, sharedEvidence));
        Assert.False(policy.IsManagedRoot(state, isolatedEvidence));
    }

    [Fact]
    public void ManagedIdentity_RejectsSharedModeWithDedicatedSharedDataArgument()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42) with
        {
            ExtensionMode = VsCodeExtensionMode.Shared,
        };
        var evidence = new ProcessIdentityEvidence(
            42,
            state.RootProcessStartTimeUtc,
            state.ExecutablePath,
            [
                state.ExecutablePath,
                "--user-data-dir",
                state.UserDataDirectory,
                "--shared-data-dir",
                state.SharedDataDirectory,
                "--new-window",
            ]);

        Assert.False(new ManagedProcessIdentityPolicy().IsManagedRoot(state, evidence));
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
    public void ManagedIdentity_RejectsProcessWithoutDedicatedSharedDataArgument()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            42,
            state.RootProcessStartTimeUtc,
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
                "--shared-data-dir",
                state.SharedDataDirectory,
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
    public void ManagedIdentity_AcceptsExistingExactMatchingManagedProcess()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            84,
            state.LaunchTimestampUtc.AddHours(-2),
            state.ExecutablePath,
            [
                state.ExecutablePath,
                "--user-data-dir",
                state.UserDataDirectory,
                "--extensions-dir",
                state.ExtensionsDirectory,
                "--shared-data-dir",
                state.SharedDataDirectory,
                "--new-window",
            ]);

        Assert.True(new ManagedProcessIdentityPolicy().IsExactManagedProcessCandidate(state, evidence));
    }

    [Fact]
    public void ManagedIdentity_RejectsOrdinaryVsCodeAsExistingCandidate()
    {
        using var temp = new TempDirectory();
        ManagedVsCodeInstanceState state = State(temp.Path, processId: 42);
        var evidence = new ProcessIdentityEvidence(
            84,
            DateTimeOffset.UtcNow,
            state.ExecutablePath,
            [state.ExecutablePath, "--new-window"]);

        Assert.False(new ManagedProcessIdentityPolicy().IsExactManagedProcessCandidate(state, evidence));
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
    public async Task RepeatedSwitching_ActivatesAlphaBetaGammaAndAlphaAgain()
    {
        using var context = new ActivationContext();

        ProfileActivationResult first = await context.ActivateAsync("alpha");
        ProfileActivationResult second = await context.ActivateAsync("beta");
        ProfileActivationResult third = await context.ActivateAsync("gamma");
        ProfileActivationResult fourth = await context.ActivateAsync("alpha");

        Assert.All(
            new[] { first, second, third, fourth },
            result => Assert.Equal(ProfileActivationStatus.Succeeded, result.Status));
        Assert.Equal("alpha", context.Layout.ActiveProfileStore.Read());
        Assert.Equal(4, context.Runtime.LaunchPlans.Count);
        Assert.Equal(3, context.Runtime.ShutdownTargets.Count);
    }

    [Fact]
    public async Task RepeatedSwitching_PreservesOneGlobalWorkspaceAcrossProfiles()
    {
        using var context = new ActivationContext();
        string workspace = Path.Combine(context.Root, "MoonRise");
        Directory.CreateDirectory(workspace);

        await context.ActivateAsync("alpha", workspace);
        await context.ActivateAsync("beta", context.WorkspaceHistory.ReadLastWorkspace());
        await context.ActivateAsync("gamma", context.WorkspaceHistory.ReadLastWorkspace());
        await context.ActivateAsync("alpha", context.WorkspaceHistory.ReadLastWorkspace());

        Assert.Equal(4, context.Runtime.LaunchPlans.Count);
        Assert.All(context.Runtime.LaunchPlans, plan => Assert.Equal(
            Path.GetFullPath(workspace),
            plan.Arguments[^1]));
        Assert.Equal(Path.GetFullPath(workspace), context.WorkspaceHistory.ReadLastWorkspace());
    }

    [Fact]
    public async Task SidebarLifecycle_DoesNotChangeSuccessfulProfileActivationResult()
    {
        using var context = new ActivationContext();
        string companionPath = Path.Combine(context.Root, "companion");
        string bridgePath = Path.Combine(context.Root, "bridge");
        Directory.CreateDirectory(companionPath);
        Directory.CreateDirectory(bridgePath);
        var companion = new ManagedCompanionLaunchOptions(
            companionPath,
            bridgePath,
            "test-session",
            true);

        ProfileActivationResult result = await context.ActivateAsync("alpha", companion: companion);

        Assert.Equal(ProfileActivationStatus.Succeeded, result.Status);
        Assert.Equal("1", context.Runtime.LaunchPlans.Single().EnvironmentOverrides[
            CompanionBridgeService.OpenCodexEnvironmentVariable]);
    }

    [Fact]
    public async Task WindowTimeout_ReleasesSwitchLockAndAllowsRetry()
    {
        using var context = new ActivationContext();
        context.Runtime.WaitResults.Enqueue(null);

        ProfileActivationResult failure = await context.ActivateAsync("alpha");
        ProfileActivationResult retry = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.WindowNotFound, failure.Status);
        Assert.Equal(ProfileActivationStatus.Succeeded, retry.Status);
        Assert.Equal("alpha", context.Layout.ActiveProfileStore.Read());
    }

    [Fact]
    public async Task ProcessExit_IsReportedWithStructuredFailureAndAllowsRetry()
    {
        using var context = new ActivationContext();
        context.Runtime.NextWaitStatus = ManagedWindowWaitStatus.ProcessExited;

        ProfileActivationResult failure = await context.ActivateAsync("alpha");
        ProfileActivationResult retry = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.LaunchFailed, failure.Status);
        Assert.Equal(ActivationFailureCategory.ProcessExitedImmediately, failure.FailureCategory);
        Assert.Equal("process-start", failure.TimeoutStage);
        Assert.Equal(ProfileActivationStatus.Succeeded, retry.Status);
    }

    [Fact]
    public void ObserveCurrent_DiscardsClearlyStaleRuntimeMetadata()
    {
        using var context = new ActivationContext();
        context.ConfigurePreviousManagedProfile("beta");
        context.Runtime.Current = null;

        ManagedVsCodeObservation? observation = context.Service.ObserveCurrent();

        Assert.Null(observation);
        Assert.Null(context.InstanceStore.Read());
        Assert.Equal("beta", context.Layout.ActiveProfileStore.Read());
    }

    [Fact]
    public async Task Cancellation_ReleasesSwitchLockAndAllowsRetry()
    {
        using var context = new ActivationContext();
        using var cancellation = new CancellationTokenSource();
        context.Runtime.WaitHandler = async (_, cancellationToken) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.ActivateAsync("alpha", cancellationToken: cancellation.Token));

        context.Runtime.WaitHandler = null;
        ProfileActivationResult retry = await context.ActivateAsync("alpha");

        Assert.Equal(ProfileActivationStatus.Succeeded, retry.Status);
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

    private static VsCodeProcessStartSpec BuildPlan(
        string root,
        string? workspace,
        VsCodeExtensionMode extensionMode = VsCodeExtensionMode.Isolated)
        => new VsCodeLaunchPlanBuilder().Build(
            Path.Combine(root, "Code.exe"),
            Path.Combine(root, "data"),
            Path.Combine(root, "extensions"),
            Path.Combine(root, "shared-data"),
            Path.Combine(root, "profile"),
            workspace,
            extensionMode);

    private static ManagedVsCodeInstanceState State(string root, int processId)
        => new(
            processId,
            DateTimeOffset.UtcNow,
            "alpha",
            null,
            Path.Combine(root, "Code.exe"),
            Path.Combine(root, "data"),
            Path.Combine(root, "extensions"),
            Path.Combine(root, "shared-data"),
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
            Layout.AddProfile("gamma", "{\"fake\":true}");
            File.WriteAllText(ExecutablePath, string.Empty);
            Directory.CreateDirectory(Layout.Paths.VsCodeUserDataDirectory);
            Directory.CreateDirectory(Layout.Paths.VsCodeExtensionsDirectory);
            Directory.CreateDirectory(Layout.Paths.VsCodeSharedDataDirectory);
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

        public Task<ProfileActivationResult> ActivateAsync(
            string profile,
            string? workspace = null,
            CancellationToken cancellationToken = default,
            ManagedCompanionLaunchOptions? companion = null)
            => Service.ActivateAsync(
                profile,
                ExecutablePath,
                Layout.Paths.VsCodeUserDataDirectory,
                Layout.Paths.VsCodeExtensionsDirectory,
                Layout.Paths.VsCodeSharedDataDirectory,
                workspace,
                TimeSpan.FromMilliseconds(50),
                restartIfAlreadyActive: false,
                requireExtension: true,
                openCodexOnStartup: true,
                extensionMode: VsCodeExtensionMode.Isolated,
                customCaVariable: CustomCaEnvironmentVariable.None,
                customCaCertificatePath: null,
                cancellationToken: cancellationToken,
                companion: companion);

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
                Layout.Paths.VsCodeSharedDataDirectory,
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
            string sharedDataDirectory,
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
                Path.GetFullPath("shared-data"),
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
        public ManagedWindowWaitStatus? NextWaitStatus { get; set; }

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

        public async Task<ManagedWindowWaitResult> WaitForWindowAsync(
            ManagedVsCodeInstanceState state,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ManagedVsCodeObservation? result;
            if (NextWaitStatus is not null
                && NextWaitStatus != ManagedWindowWaitStatus.WindowFound)
            {
                result = null;
            }
            else if (WaitHandler is not null)
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
            ManagedWindowWaitStatus status = NextWaitStatus
                ?? (result is not null
                    ? ManagedWindowWaitStatus.WindowFound
                    : Current is not null
                        ? ManagedWindowWaitStatus.MatchingProcessWithoutWindow
                        : ManagedWindowWaitStatus.WindowDetectionTimeout);
            NextWaitStatus = null;
            return new ManagedWindowWaitResult(
                status,
                Current,
                TimeSpan.FromMilliseconds(1));
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
