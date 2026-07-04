using System.Text.Json;
using CodexProfileOverlay.Core.Models;

namespace CodexProfileOverlay.Core.Services;

public sealed class ProfileStatusStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string statusFile;
    private readonly object lockObject = new();

    public ProfileStatusStore(string statusFile)
    {
        this.statusFile = Path.GetFullPath(statusFile);
    }

    public ProfileStatusDocument Load()
    {
        lock (lockObject)
        {
            if (!File.Exists(statusFile))
            {
                return new ProfileStatusDocument();
            }

            using FileStream stream = File.OpenRead(statusFile);
            return JsonSerializer.Deserialize<ProfileStatusDocument>(stream, SerializerOptions) ?? new ProfileStatusDocument();
        }
    }

    public void Save(ProfileStatusDocument document)
    {
        lock (lockObject)
        {
            ArgumentNullException.ThrowIfNull(document);
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
}