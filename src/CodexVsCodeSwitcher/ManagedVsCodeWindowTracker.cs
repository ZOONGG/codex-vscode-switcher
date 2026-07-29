using System.Windows.Threading;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher;

internal sealed class ManagedVsCodeWindowTracker : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Func<ManagedVsCodeObservation?> observe;
    private readonly ManagedOverlayVisibilityPolicy visibilityPolicy = new();
    private readonly List<IntPtr> eventHooks = [];
    private readonly NativeMethods.WinEventDelegate eventCallback;
    private bool disposed;

    public ManagedVsCodeWindowTracker(
        Dispatcher dispatcher,
        Func<ManagedVsCodeObservation?> observe)
    {
        this.dispatcher = dispatcher;
        this.observe = observe;
        eventCallback = OnWinEvent;
    }

    public event Action<ManagedVsCodeObservation?, bool>? StateChanged;

    public IntPtr OverlayHandle { get; set; }

    public bool ShowOnlyWithManagedVsCode { get; set; } = true;

    public bool ManualVisibilityRequested { get; set; } = true;

    public void Start()
    {
        RegisterHook(NativeMethods.EventSystemForeground);
        RegisterHook(NativeMethods.EventSystemMinimizeStart);
        RegisterHook(NativeMethods.EventSystemMinimizeEnd);
        RegisterHook(NativeMethods.EventObjectDestroy);
        RegisterHook(NativeMethods.EventObjectShow);
        RegisterHook(NativeMethods.EventObjectHide);
        RegisterHook(NativeMethods.EventObjectLocationChange);
        Reconcile();
    }

    public void Reconcile()
    {
        if (disposed)
        {
            return;
        }

        ManagedVsCodeObservation? observation = observe();
        ManagedVsCodeWindow? managedWindow = observation?.Window;
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        bool managedForeground = observation is not null
            && foreground != IntPtr.Zero
            && WindowBelongsToProcesses(foreground, observation.VerifiedProcessIds);
        bool overlayForeground = foreground != IntPtr.Zero
            && OverlayHandle != IntPtr.Zero
            && (foreground == OverlayHandle || IsOwnedBy(foreground, OverlayHandle));
        bool shouldShow = visibilityPolicy.ShouldShow(new ManagedOverlayVisibilityInput(
            managedWindow is not null && NativeMethods.IsWindow(managedWindow.Handle),
            managedWindow?.IsVisible == true,
            managedWindow?.IsMinimized == true,
            managedForeground,
            overlayForeground,
            NativeMethods.IsInputDesktopAvailable(),
            ShowOnlyWithManagedVsCode,
            ManualVisibilityRequested));
        StateChanged?.Invoke(observation, shouldShow);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (IntPtr hook in eventHooks)
        {
            _ = NativeMethods.UnhookWinEvent(hook);
        }

        eventHooks.Clear();
    }

    private void RegisterHook(uint eventId)
    {
        IntPtr hook = NativeMethods.SetWinEventHook(
            eventId,
            eventId,
            IntPtr.Zero,
            eventCallback,
            0,
            0,
            NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnThread);
        if (hook != IntPtr.Zero)
        {
            eventHooks.Add(hook);
        }
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        _ = hook;
        _ = eventThread;
        _ = eventTime;
        if (eventType >= NativeMethods.EventObjectDestroy
            && objectId != NativeMethods.ObjIdWindow
            && objectId != 0)
        {
            return;
        }

        _ = window;
        _ = childId;
        _ = dispatcher.BeginInvoke(DispatcherPriority.Send, Reconcile);
    }

    private static bool WindowBelongsToProcesses(IntPtr window, IReadOnlySet<int> processIds)
    {
        _ = NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        return processIds.Contains((int)processId);
    }

    private static bool IsOwnedBy(IntPtr window, IntPtr possibleOwner)
    {
        var visited = new HashSet<IntPtr>();
        IntPtr current = window;
        while (current != IntPtr.Zero && visited.Add(current))
        {
            current = NativeMethods.GetWindow(current, NativeMethods.GwOwner);
            if (current == possibleOwner)
            {
                return true;
            }
        }

        return false;
    }
}
