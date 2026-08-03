namespace CodexVsCodeSwitcher.Core.Services;

public sealed record PendingProjectActivation(string ProfileId);

public sealed record ResolvedProjectActivation(string ProfileId, string? ProjectPath);

public sealed class ProjectLaunchContinuation(IProtectedPathPolicy protectedPaths)
{
    public PendingProjectActivation Begin(string profileId)
    {
        if (!ProfileName.IsValid(profileId))
        {
            throw new ArgumentException("The pending profile identifier is invalid.", nameof(profileId));
        }

        return new PendingProjectActivation(profileId);
    }

    public ResolvedProjectActivation SelectFolder(
        PendingProjectActivation pending,
        string selectedPath)
    {
        string path = NormalizeSelectedPath(selectedPath);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"The selected project folder is unavailable: {path}");
        }

        return new ResolvedProjectActivation(pending.ProfileId, path);
    }

    public ResolvedProjectActivation SelectWorkspaceFile(
        PendingProjectActivation pending,
        string selectedPath)
    {
        string path = NormalizeSelectedPath(selectedPath);
        if (!File.Exists(path)
            || !Path.GetExtension(path).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(
                $"The selected .code-workspace project is unavailable: {path}",
                path);
        }

        return new ResolvedProjectActivation(pending.ProfileId, path);
    }

    public ResolvedProjectActivation OpenWithoutProject(PendingProjectActivation pending)
        => new(pending.ProfileId, null);

    public async Task ResumeAsync(
        ResolvedProjectActivation selection,
        Action<string?> persistSelection,
        Func<string, Task> activateProfileAsync)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(persistSelection);
        ArgumentNullException.ThrowIfNull(activateProfileAsync);

        // Persist the current/recent project before control returns to the asynchronous
        // activation pipeline. The pending profile is carried by the immutable selection.
        persistSelection(selection.ProjectPath);
        await activateProfileAsync(selection.ProfileId).ConfigureAwait(false);
    }

    private string NormalizeSelectedPath(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)
            || selectedPath.IndexOf('\0') >= 0
            || ContainsTraversal(selectedPath))
        {
            throw new ArgumentException("The selected project path is invalid.", nameof(selectedPath));
        }

        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedPath.Trim()));
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The selected project path must be fully qualified.", nameof(selectedPath));
        }

        protectedPaths.AssertCanRead(path);
        return path;
    }

    private static bool ContainsTraversal(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment == "..");
}
