using System.Text.Json;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ProfileMetadataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string metadataFile;
    private readonly IProtectedPathPolicy? protectedPaths;

    public ProfileMetadataStore(string metadataFile, IProtectedPathPolicy? protectedPaths = null)
    {
        this.metadataFile = Path.GetFullPath(metadataFile);
        this.protectedPaths = protectedPaths;
        protectedPaths?.AssertCanWrite(this.metadataFile);
    }

    public ProfileMetadataDocument Load()
    {
        if (!File.Exists(metadataFile))
        {
            return new ProfileMetadataDocument();
        }

        using FileStream stream = File.OpenRead(metadataFile);
        return JsonSerializer.Deserialize<ProfileMetadataDocument>(stream, SerializerOptions) ?? new ProfileMetadataDocument();
    }

    public void Save(ProfileMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        protectedPaths?.AssertCanWrite(metadataFile);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataFile)!);

        string temp = metadataFile + ".tmp";
        using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, document, SerializerOptions);
        }

        if (File.Exists(metadataFile))
        {
            File.Replace(temp, metadataFile, null);
        }
        else
        {
            File.Move(temp, metadataFile);
        }
    }
}
