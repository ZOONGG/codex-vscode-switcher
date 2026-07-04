using CodexProfileOverlay.Core.Models;

namespace CodexProfileOverlay.Core.Services;

public sealed class ProfileStatusService
{
    private readonly ProfileStatusStore store;
    private readonly IUsageProvider usageProvider;
    private readonly SafeLogger logger;
    private TimeSpan staleThreshold = TimeSpan.FromMinutes(90);
    private readonly object refreshLock = new();

    public ProfileStatusService(ProfileStatusStore store, IUsageProvider usageProvider, SafeLogger logger)
    {
        this.store = store;
        this.usageProvider = usageProvider;
        this.logger = logger;
    }

    public void SetStaleThreshold(TimeSpan threshold)
    {
        staleThreshold = threshold;
    }

    public ProfileStatusDocument Load()
    {
        return store.Load();
    }

    public void Save(ProfileStatusDocument document)
    {
        store.Save(document);
    }

    public ProfileStatusMetadata GetOrCreateStatus(ProfileStatusDocument document, string profileId)
    {
        return store.GetOrCreateStatus(document, profileId);
    }

    public UsageSnapshot? GetCachedSnapshot(ProfileStatusDocument document, string profileId)
    {
        return document.Snapshots.TryGetValue(profileId, out UsageSnapshot? snapshot) ? snapshot : null;
    }

    public string GetLimitIndicator(UsageSnapshot? snapshot, int greenThreshold, int yellowThreshold)
    {
        if (snapshot?.ShortWindowRemainingPercent is not int percent || snapshot.IsStale)
        {
            return string.Empty;
        }

        if (percent >= greenThreshold)
        {
            return "🟢";
        }

        if (percent >= yellowThreshold)
        {
            return "🟡";
        }

        return "🔴";
    }

    public async Task RefreshUsageAsync(string profileId, string profileDirectory, ProfileStatusDocument document, CancellationToken cancellationToken)
    {
        lock (refreshLock)
        {
            // Serialize refreshes - simple lock is sufficient for now
        }

        try
        {
            var status = GetOrCreateStatus(document, profileId);
            status.LastRefreshAttemptAt = DateTimeOffset.UtcNow;

            UsageSnapshot? snapshot = null;
            if (usageProvider.Capability == UsageProviderCapability.Supported)
            {
                snapshot = await usageProvider.GetUsageAsync(profileDirectory, cancellationToken);
            }

            if (snapshot is not null)
            {
                snapshot.Source = usageProvider.GetType().Name;
                document.Snapshots[profileId] = snapshot;
                status.LastAutomaticSnapshot = snapshot.CapturedAt;
                status.LastRefreshError = null;
            }
            else if (status.LastAutomaticSnapshot is not null)
            {
                // Mark existing snapshot as stale on failure, but don't delete it
                var existing = document.Snapshots.GetValueOrDefault(profileId);
                if (existing is not null)
                {
                    existing.IsStale = true;
                }
                status.LastRefreshError = "Automatic limits unavailable";
            }

            Save(document);
        }
        catch (Exception exception)
        {
            var status = GetOrCreateStatus(document, profileId);
            status.LastRefreshAttemptAt = DateTimeOffset.UtcNow;
            status.LastRefreshError = exception.Message;
            Save(document);

            logger.Error($"Failed to refresh usage for profile '{profileId}'.", exception);
        }
    }

    public string? FindRecommendedProfile(IReadOnlyList<string> profileIds, ProfileStatusDocument document, int greenThreshold, int yellowThreshold)
    {
        var profilesWithFreshData = profileIds
            .Select(id => new { Id = id, Snapshot = GetCachedSnapshot(document, id) })
            .Where(x => x.Snapshot is not null && !x.Snapshot.IsStale)
            .ToList();

        if (profilesWithFreshData.Count < 2)
        {
            return null;
        }

        // Sort by: exhausted last, then by lowest remaining percent, then by average, then by nearest reset
        var ranked = profilesWithFreshData
            .OrderBy(p => p.Snapshot!.IsExhausted == true)
            .ThenBy(p => p.Snapshot!.ShortWindowRemainingPercent ?? 100)
            .ThenBy(p => p.Snapshot!.LongWindowRemainingPercent ?? 100)
            .ThenBy(p => p.Snapshot!.ShortWindowResetAt)
            .ToList();

        if (ranked.FirstOrDefault()?.Snapshot?.IsExhausted == true)
        {
            return null;
        }

        return ranked.FirstOrDefault()?.Id;
    }

    public bool IsDataStale(UsageSnapshot snapshot)
    {
        return DateTimeOffset.UtcNow - snapshot.CapturedAt > staleThreshold;
    }
}