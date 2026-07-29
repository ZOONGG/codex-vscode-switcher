using System.Text.Json;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ProfileStatusStore
{
    private const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string statusFile;
    private readonly IProtectedPathPolicy? protectedPaths;
    private readonly object lockObject = new();

    public ProfileStatusStore(string statusFile, IProtectedPathPolicy? protectedPaths = null)
    {
        this.statusFile = Path.GetFullPath(statusFile);
        this.protectedPaths = protectedPaths;
        protectedPaths?.AssertCanWrite(this.statusFile);
    }

    public ProfileStatusDocument Load()
    {
        lock (lockObject)
        {
            if (!File.Exists(statusFile))
            {
                return new ProfileStatusDocument();
            }

            try
            {
                using FileStream stream = File.OpenRead(statusFile);
                return Normalize(JsonSerializer.Deserialize<ProfileStatusDocument>(stream, SerializerOptions));
            }
            catch (JsonException)
            {
                return new ProfileStatusDocument();
            }
            catch (IOException)
            {
                return new ProfileStatusDocument();
            }
        }
    }

    public void Save(ProfileStatusDocument document)
    {
        lock (lockObject)
        {
            ArgumentNullException.ThrowIfNull(document);
            protectedPaths?.AssertCanWrite(statusFile);
            Normalize(document);
            Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!);

            string temp = statusFile + ".tmp";
            using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, SerializerOptions);
            }

            if (File.Exists(statusFile))
            {
                File.Replace(temp, statusFile, null);
            }
            else
            {
                File.Move(temp, statusFile);
            }
        }
    }

    public ProfileStatusMetadata GetOrCreateStatus(ProfileStatusDocument document, string profileId)
    {
        ProfileStatusMetadata? status = document.Profiles.FirstOrDefault(p => p.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (status is null)
        {
            status = new ProfileStatusMetadata { ProfileId = profileId };
            document.Profiles.Add(status);
        }
        return status;
    }

    private static ProfileStatusDocument Normalize(ProfileStatusDocument? document)
    {
        document ??= new ProfileStatusDocument();
        bool enableAutomaticRefreshForLegacyProfiles = document.SchemaVersion < CurrentSchemaVersion;
        document.SchemaVersion = CurrentSchemaVersion;
        document.Profiles ??= [];
        document.Snapshots ??= new Dictionary<string, UsageSnapshot>(StringComparer.OrdinalIgnoreCase);
        document.Snapshots = new Dictionary<string, UsageSnapshot>(document.Snapshots, StringComparer.OrdinalIgnoreCase);

        foreach (ProfileStatusMetadata status in document.Profiles)
        {
            if (enableAutomaticRefreshForLegacyProfiles)
            {
                status.AutomaticRefreshEnabled = true;
            }

            status.ProfileId = status.ProfileId.Trim();
            status.ManualResetAt = status.ManualResetAt?.ToUniversalTime();
            status.LastAutomaticSnapshot = status.LastAutomaticSnapshot?.ToUniversalTime();
            status.LastRefreshAttemptAt = status.LastRefreshAttemptAt?.ToUniversalTime();
        }

        foreach (UsageSnapshot snapshot in document.Snapshots.Values)
        {
            snapshot.Windows ??= [];
            snapshot.CapturedAt = snapshot.CapturedAt.ToUniversalTime();
            snapshot.ShortWindowResetAt = snapshot.ShortWindowResetAt?.ToUniversalTime();
            snapshot.LongWindowResetAt = snapshot.LongWindowResetAt?.ToUniversalTime();
            foreach (UsageLimitWindow window in snapshot.Windows)
            {
                window.Name = window.Name.Trim();
                window.ResetAt = window.ResetAt?.ToUniversalTime();
            }
        }

        return document;
    }
}
