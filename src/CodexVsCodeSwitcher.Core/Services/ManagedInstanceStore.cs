using System.Text.Json;
using System.Text.Json.Serialization;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ManagedInstanceStore : IManagedInstanceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string filePath;
    private readonly IProtectedPathPolicy protectedPaths;

    public ManagedInstanceStore(string filePath, IProtectedPathPolicy protectedPaths)
    {
        this.filePath = Path.GetFullPath(filePath);
        this.protectedPaths = protectedPaths;
        protectedPaths.AssertCanWrite(this.filePath);
    }

    public ManagedVsCodeInstanceState? Read()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        protectedPaths.AssertCanRead(filePath);
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            ManagedVsCodeInstanceState? state =
                JsonSerializer.Deserialize<ManagedVsCodeInstanceState>(stream, SerializerOptions);
            return IsSane(state) ? Normalize(state!) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Write(ManagedVsCodeInstanceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsSane(state))
        {
            throw new ArgumentException("Managed instance metadata is invalid.", nameof(state));
        }

        AtomicJsonFile.Write(filePath, Normalize(state), SerializerOptions, protectedPaths);
    }

    public void Clear()
    {
        protectedPaths.AssertCanWrite(filePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static bool IsSane(ManagedVsCodeInstanceState? state)
        => state is not null
            && state.RootProcessId > 0
            && state.RootProcessStartTimeUtc != default
            && ProfileName.IsValid(state.SelectedProfileId)
            && Path.IsPathFullyQualified(state.ExecutablePath)
            && Path.IsPathFullyQualified(state.UserDataDirectory)
            && Path.IsPathFullyQualified(state.ExtensionsDirectory)
            && (state.WorkspacePath is null || Path.IsPathFullyQualified(state.WorkspacePath));

    private static ManagedVsCodeInstanceState Normalize(ManagedVsCodeInstanceState state)
        => state with
        {
            ExecutablePath = Path.GetFullPath(state.ExecutablePath),
            UserDataDirectory = Path.GetFullPath(state.UserDataDirectory),
            ExtensionsDirectory = Path.GetFullPath(state.ExtensionsDirectory),
            WorkspacePath = string.IsNullOrWhiteSpace(state.WorkspacePath)
                ? null
                : Path.GetFullPath(state.WorkspacePath),
        };
}
