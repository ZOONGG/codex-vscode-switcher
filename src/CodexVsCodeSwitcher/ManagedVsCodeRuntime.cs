using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher;

internal sealed class ManagedVsCodeRuntime : IManagedVsCodeRuntime
{
    private readonly ManagedProcessIdentityPolicy identityPolicy = new();

    public ManagedVsCodeObservation? Observe(ManagedVsCodeInstanceState state)
        => ObserveExact(state) ?? TryObserveTransferredRoot(state);

    private ManagedVsCodeObservation? ObserveExact(ManagedVsCodeInstanceState state)
    {
        if (!TryOpenVerifiedRoot(state, out Process? root))
        {
            return null;
        }

        using (root)
        {
            IReadOnlyDictionary<int, int> parentByProcess = SnapshotParentProcesses();
            HashSet<int> descendants = CollectDescendants(state.RootProcessId, parentByProcess);
            var verified = new HashSet<int>();
            foreach (int processId in descendants)
            {
                if (TryVerifyExecutable(processId, state.ExecutablePath))
                {
                    verified.Add(processId);
                }
            }

            if (!verified.Contains(state.RootProcessId))
            {
                return null;
            }

            ManagedVsCodeWindow? window = FindBestWindow(verified);
            return new ManagedVsCodeObservation(state, verified, window);
        }
    }

    public async Task<ManagedShutdownResult> RequestCloseAsync(
        ManagedVsCodeObservation instance,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ManagedVsCodeObservation? verified = Observe(instance.State);
        if (verified is null)
        {
            return new ManagedShutdownResult(ManagedShutdownStatus.NotRunning, new HashSet<int>());
        }

        IReadOnlyList<ManagedVsCodeWindow> windows = FindWindows(verified.VerifiedProcessIds);
        if (windows.Count == 0)
        {
            return new ManagedShutdownResult(ManagedShutdownStatus.InvalidTarget, verified.VerifiedProcessIds);
        }

        foreach (ManagedVsCodeWindow window in windows)
        {
            _ = NativeMethods.PostMessage(window.Handle, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);
        }
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Observe(instance.State) is null)
            {
                return new ManagedShutdownResult(ManagedShutdownStatus.Closed, verified.VerifiedProcessIds);
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return new ManagedShutdownResult(ManagedShutdownStatus.Blocked, verified.VerifiedProcessIds);
    }

    public ManagedProcessIdentity Launch(VsCodeProcessStartSpec startSpec)
    {
        Process process = Process.Start(ProcessCommandRunner.CreateStartInfo(startSpec))
            ?? throw new InvalidOperationException("The managed VS Code process could not be started.");
        using (process)
        {
            return new ManagedProcessIdentity(
                process.Id,
                new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero));
        }
    }

    public async Task<ManagedVsCodeObservation?> WaitForWindowAsync(
        ManagedVsCodeInstanceState state,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedVsCodeObservation? observation = Observe(state);
            if (observation?.Window is { IsVisible: true })
            {
                return observation;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public void ForceClose(ManagedVsCodeObservation instance)
    {
        ManagedVsCodeObservation? verified = Observe(instance.State);
        if (verified is null)
        {
            return;
        }

        foreach (int processId in verified.VerifiedProcessIds.OrderByDescending(static value => value))
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (TryVerifyExecutable(processId, instance.State.ExecutablePath) && !process.HasExited)
                {
                    process.Kill(entireProcessTree: false);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }
    }

    private bool TryOpenVerifiedRoot(ManagedVsCodeInstanceState state, out Process? process)
    {
        process = null;
        try
        {
            process = Process.GetProcessById(state.RootProcessId);
            string? executable = process.MainModule?.FileName;
            string? commandLine = NativeMethods.TryReadProcessCommandLine(process.Id);
            if (process.HasExited
                || executable is null
                || commandLine is null
                || !identityPolicy.IsManagedRoot(
                    state,
                    new ProcessIdentityEvidence(
                        process.Id,
                        new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
                        executable,
                        NativeMethods.ParseCommandLine(commandLine))))
            {
                process.Dispose();
                process = null;
                return false;
            }

            return true;
        }
        catch (ArgumentException)
        {
            process?.Dispose();
            process = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            process?.Dispose();
            process = null;
            return false;
        }
        catch (Win32Exception)
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }

    private ManagedVsCodeObservation? TryObserveTransferredRoot(ManagedVsCodeInstanceState state)
    {
        IReadOnlyDictionary<int, int> parentByProcess = SnapshotParentProcesses();
        HashSet<int> descendants = CollectDescendants(state.RootProcessId, parentByProcess);
        var candidates = new List<ProcessIdentityEvidence>();
        foreach (int processId in descendants)
        {
            if (processId == state.RootProcessId
                || !TryReadIdentityEvidence(processId, out ProcessIdentityEvidence evidence)
                || !identityPolicy.IsManagedRootHandoffCandidate(state, evidence))
            {
                continue;
            }

            candidates.Add(evidence);
        }

        ManagedVsCodeObservation? fallback = null;
        foreach (ProcessIdentityEvidence candidate in candidates.OrderBy(static item => item.StartTimeUtc))
        {
            ManagedVsCodeInstanceState recovered = state with
            {
                RootProcessId = candidate.ProcessId,
                RootProcessStartTimeUtc = candidate.StartTimeUtc,
            };
            ManagedVsCodeObservation? observation = ObserveExact(recovered);
            if (observation?.Window is not null)
            {
                return observation;
            }

            fallback ??= observation;
        }

        return fallback;
    }

    private static bool TryReadIdentityEvidence(
        int processId,
        out ProcessIdentityEvidence evidence)
    {
        evidence = null!;
        try
        {
            using Process process = Process.GetProcessById(processId);
            string? executable = process.MainModule?.FileName;
            string? commandLine = NativeMethods.TryReadProcessCommandLine(process.Id);
            if (process.HasExited || executable is null || commandLine is null)
            {
                return false;
            }

            evidence = new ProcessIdentityEvidence(
                process.Id,
                new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
                executable,
                NativeMethods.ParseCommandLine(commandLine));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool TryVerifyExecutable(int processId, string expectedExecutable)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            string? actual = process.MainModule?.FileName;
            return actual is not null
                && Path.GetFullPath(actual).Equals(
                    Path.GetFullPath(expectedExecutable),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<int, int> SnapshotParentProcesses()
    {
        var result = new Dictionary<int, int>();
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32csSnapProcess, 0);
        if (snapshot == NativeMethods.InvalidHandleValue)
        {
            return result;
        }

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
            };
            if (!NativeMethods.Process32First(snapshot, ref entry))
            {
                return result;
            }

            do
            {
                result[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));
        }
        finally
        {
            _ = NativeMethods.CloseHandle(snapshot);
        }

        return result;
    }

    private static HashSet<int> CollectDescendants(
        int rootProcessId,
        IReadOnlyDictionary<int, int> parentByProcess)
    {
        var result = new HashSet<int> { rootProcessId };
        bool added;
        do
        {
            added = false;
            foreach ((int processId, int parentId) in parentByProcess)
            {
                if (result.Contains(parentId) && result.Add(processId))
                {
                    added = true;
                }
            }
        }
        while (added);
        return result;
    }

    private static ManagedVsCodeWindow? FindBestWindow(IReadOnlySet<int> processIds)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        return FindWindows(processIds)
            .OrderByDescending(window => window.Handle == foreground)
            .ThenByDescending(static window =>
            {
                _ = NativeMethods.GetWindowRect(window.Handle, out NativeRect bounds);
                return (long)bounds.Width * bounds.Height;
            })
            .FirstOrDefault();
    }

    private static IReadOnlyList<ManagedVsCodeWindow> FindWindows(IReadOnlySet<int> processIds)
    {
        var windows = new List<(ManagedVsCodeWindow Window, long Area)>();
        _ = NativeMethods.EnumWindows((handle, callbackState) =>
        {
            _ = callbackState;
            _ = NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
            if (!processIds.Contains((int)processId)
                || NativeMethods.GetWindow(handle, NativeMethods.GwOwner) != IntPtr.Zero
                || !NativeMethods.IsWindow(handle)
                || !NativeMethods.GetWindowRect(handle, out NativeRect bounds)
                || bounds.Width <= 0
                || bounds.Height <= 0)
            {
                return true;
            }

            try
            {
                using Process process = Process.GetProcessById((int)processId);
                var window = new ManagedVsCodeWindow(
                    handle,
                    (int)processId,
                    new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
                    NativeMethods.IsWindowVisible(handle),
                    NativeMethods.IsIconic(handle));
                windows.Add((window, (long)bounds.Width * bounds.Height));
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }

            return true;
        }, IntPtr.Zero);

        return windows
            .Where(static item => item.Window.IsVisible)
            .Select(static item => item.Window)
            .ToArray();
    }
}
