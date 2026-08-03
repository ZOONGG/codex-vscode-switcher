using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ProfileDiscoveryService
{
    private readonly string profilesDirectory;
    private readonly IProtectedPathPolicy protectedPaths;
    private readonly ProfileStorageAuditService auditService;

    public ProfileDiscoveryService(string profilesDirectory, IProtectedPathPolicy protectedPaths)
    {
        this.profilesDirectory = Path.GetFullPath(profilesDirectory);
        this.protectedPaths = protectedPaths;
        auditService = new ProfileStorageAuditService(this.profilesDirectory, protectedPaths);
    }

    public IReadOnlyList<ProfileInfo> DiscoverProfiles()
    {
        if (!Directory.Exists(profilesDirectory))
        {
            return Array.Empty<ProfileInfo>();
        }

        return auditService.AuditProfiles()
            .Select(CreateProfileInfo)
            .OrderBy(static profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public ProfileInfo GetRequiredProfile(string profileName)
    {
        string validName = ProfileName.RequireValid(profileName);
        string directory = Path.Combine(profilesDirectory, validName);
        string fullDirectory = Path.GetFullPath(directory);
        EnsureInsideProfilesRoot(fullDirectory);
        protectedPaths.AssertCanRead(fullDirectory);

        ProfileStorageAudit audit = auditService.AuditRequiredProfile(validName);
        if (!audit.IsEligibleForSwitching)
        {
            throw new InvalidDataException($"Profile '{validName}' is not eligible for switching.");
        }

        return CreateProfileInfo(audit);
    }

    private static ProfileInfo CreateProfileInfo(ProfileStorageAudit audit)
        => new(
            audit.ProfileName,
            audit.DirectoryPath,
            Path.Combine(audit.DirectoryPath, "auth.json"))
        {
            ValidationStatus = audit.Status,
            DirectorySizeBytes = audit.DirectorySizeBytes,
            IgnoredRuntimeFileCount = audit.IgnoredRuntimeFileCount,
            HasConfigFile = audit.HasConfigFile,
        };

    private void EnsureInsideProfilesRoot(string fullPath)
    {
        string root = Path.TrimEndingDirectorySeparator(profilesDirectory) + Path.DirectorySeparatorChar;
        string candidate = Path.TrimEndingDirectorySeparator(fullPath) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved profile path escapes the profiles directory.");
        }
    }
}
