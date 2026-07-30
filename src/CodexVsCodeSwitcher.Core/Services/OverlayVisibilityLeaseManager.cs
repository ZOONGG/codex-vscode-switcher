namespace CodexVsCodeSwitcher.Core.Services;

[Flags]
public enum OverlayVisibilityReason
{
    None = 0,
    ManagedVsCodeForeground = 1 << 0,
    OverlayInteraction = 1 << 1,
    SettingsPreview = 1 << 2,
    ProfileSwitchingStatus = 1 << 3,
}

public sealed class OverlayVisibilityLeaseManager
{
    private readonly object sync = new();
    private readonly Dictionary<OverlayVisibilityReason, int> leaseCounts = [];

    public OverlayVisibilityReason ActiveReasons
    {
        get
        {
            lock (sync)
            {
                return leaseCounts.Keys.Aggregate(
                    OverlayVisibilityReason.None,
                    static (current, reason) => current | reason);
            }
        }
    }

    public IDisposable Acquire(OverlayVisibilityReason reason)
    {
        if (reason is OverlayVisibilityReason.None
            || !Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        lock (sync)
        {
            leaseCounts.TryGetValue(reason, out int current);
            leaseCounts[reason] = checked(current + 1);
        }

        return new VisibilityLease(this, reason);
    }

    private void Release(OverlayVisibilityReason reason)
    {
        lock (sync)
        {
            if (!leaseCounts.TryGetValue(reason, out int current))
            {
                return;
            }

            if (current == 1)
            {
                leaseCounts.Remove(reason);
            }
            else
            {
                leaseCounts[reason] = current - 1;
            }
        }
    }

    private sealed class VisibilityLease(
        OverlayVisibilityLeaseManager owner,
        OverlayVisibilityReason reason) : IDisposable
    {
        private OverlayVisibilityLeaseManager? currentOwner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref currentOwner, null)?.Release(reason);
    }
}
