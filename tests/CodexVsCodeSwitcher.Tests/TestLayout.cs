using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

internal sealed class TestLayout : IDisposable
{
    private readonly TempDirectory tempDirectory = new();

    public TestLayout()
    {
        UserProfile = Path.Combine(tempDirectory.Path, "user");
        LocalAppData = Path.Combine(tempDirectory.Path, "local");
        Paths = new CodexVsCodeStorageLayout(UserProfile, LocalAppData);
        ProtectedPaths = ProtectedPathPolicy.FromLayout(Paths);
        Directory.CreateDirectory(Paths.ProfilesDirectory);
        ActiveProfileStore = new ActiveProfileStore(Paths.ActiveProfileFile, ProtectedPaths);
    }

    public string UserProfile { get; }
    public string LocalAppData { get; }
    public CodexVsCodeStorageLayout Paths { get; }
    public IProtectedPathPolicy ProtectedPaths { get; }
    public ActiveProfileStore ActiveProfileStore { get; }

    public void AddProfile(string name, string authContent)
    {
        string directory = Path.Combine(Paths.ProfilesDirectory, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "auth.json"), authContent);
    }

    public string ReadProfileAuth(string name)
        => File.ReadAllText(Path.Combine(Paths.ProfilesDirectory, name, "auth.json"));

    public void Dispose() => tempDirectory.Dispose();
}
