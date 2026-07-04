namespace CodexProfileOverlay.Core.Models;

public sealed class ProfileStatusDocument
{
    public List<ProfileStatusMetadata> Profiles { get; set; } = [];

    public Dictionary<string, UsageSnapshot> Snapshots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}