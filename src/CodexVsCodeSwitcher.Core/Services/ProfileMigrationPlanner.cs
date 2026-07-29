namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ProfileMigrationPlanner
{
    private static readonly HashSet<string> AllowedFiles =
        new(StringComparer.OrdinalIgnoreCase) { "auth.json", "config.toml" };

    private readonly CodexVsCodeStorageLayout layout;

    public ProfileMigrationPlanner(CodexVsCodeStorageLayout layout)
    {
        this.layout = layout;
    }

    public IReadOnlyList<ProfileMigrationCandidate> CreatePlan()
    {
        if (!Directory.Exists(layout.LegacyProfilesDirectory))
        {
            return Array.Empty<ProfileMigrationCandidate>();
        }

        var result = new List<ProfileMigrationCandidate>();
        foreach (string directory in Directory.EnumerateDirectories(layout.LegacyProfilesDirectory))
        {
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !ProfileName.IsValid(info.Name))
            {
                continue;
            }

            var files = new List<ProfileMigrationFile>();
            foreach (FileInfo file in info.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0
                    || !AllowedFiles.Contains(file.Name)
                    || file.Length > MinimalBackupService.MaximumFileBytes)
                {
                    continue;
                }

                files.Add(new ProfileMigrationFile(file.Name, file.FullName, file.Length));
            }

            result.Add(new ProfileMigrationCandidate(
                info.Name,
                info.FullName,
                Path.Combine(layout.ProfilesDirectory, info.Name),
                files));
        }

        return result.OrderBy(static item => item.ProfileId, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

public sealed record ProfileMigrationCandidate(
    string ProfileId,
    string SourceDirectory,
    string DestinationDirectory,
    IReadOnlyList<ProfileMigrationFile> AllowedFiles);

public sealed record ProfileMigrationFile(string FileName, string SourcePath, long Size);
