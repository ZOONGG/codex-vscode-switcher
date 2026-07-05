namespace CodexProfileOverlay.Core.Models;

public sealed class ProfileStatusDocument
{
    public int SchemaVersion { get; set; } = 2;

    public List<ProfileStatusMetadata> Profiles { get; set; } = [];

    public Dictionary<string, UsageSnapshot> Snapshots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
