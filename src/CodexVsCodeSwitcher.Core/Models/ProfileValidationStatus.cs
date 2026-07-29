namespace CodexVsCodeSwitcher.Core.Models;

public enum ProfileValidationStatus
{
    Valid,
    Invalid,
    Incomplete,
}

public sealed record ProfileStorageAudit(
    string ProfileName,
    string DirectoryPath,
    ProfileValidationStatus Status,
    long DirectorySizeBytes,
    int IgnoredRuntimeFileCount,
    bool HasAuthFile,
    bool HasConfigFile)
{
    public bool IsEligibleForSwitching => Status == ProfileValidationStatus.Valid;
}
