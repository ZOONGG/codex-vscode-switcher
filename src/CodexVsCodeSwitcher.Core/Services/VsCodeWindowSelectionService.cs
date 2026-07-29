namespace CodexVsCodeSwitcher.Core.Services;

public sealed class VsCodeWindowSelectionService
{
    private static readonly HashSet<string> StandardExecutableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code.exe",
        "Code - Insiders.exe",
    };

    public VsCodeWindowCandidate? Select(
        IEnumerable<VsCodeWindowCandidate> candidates,
        string? customExecutablePath,
        bool includeStandardExecutables = true)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        string normalizedCustomPath = NormalizeOptionalPath(customExecutablePath);
        return candidates
            .Where(candidate => candidate.Handle != 0
                && candidate.ProcessId > 0
                && candidate.IsVisible
                && candidate.IsTopLevel
                && candidate.Width > 0
                && candidate.Height > 0
                && IsSupportedExecutable(candidate.ExecutablePath, normalizedCustomPath, includeStandardExecutables))
            .OrderByDescending(static candidate => candidate.Width * candidate.Height)
            .ThenBy(static candidate => candidate.ProcessId)
            .FirstOrDefault();
    }

    public bool IsSupportedExecutable(
        string? executablePath,
        string? customExecutablePath,
        bool includeStandardExecutables = true)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        string normalizedExecutable = NormalizePathOrFileName(executablePath);
        string normalizedCustom = NormalizeOptionalPath(customExecutablePath);
        if (normalizedCustom.Length > 0
            && Path.IsPathFullyQualified(normalizedExecutable)
            && normalizedExecutable.Equals(normalizedCustom, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return includeStandardExecutables
            && StandardExecutableNames.Contains(Path.GetFileName(normalizedExecutable));
    }

    private static string NormalizeOptionalPath(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Path.GetFullPath(value.Trim());

    private static string NormalizePathOrFileName(string value)
    {
        string trimmed = value.Trim();
        return Path.IsPathFullyQualified(trimmed) ? Path.GetFullPath(trimmed) : trimmed;
    }
}

public sealed record VsCodeWindowCandidate(
    int ProcessId,
    string ExecutablePath,
    nint Handle,
    bool IsVisible,
    bool IsTopLevel,
    int Width,
    int Height);
