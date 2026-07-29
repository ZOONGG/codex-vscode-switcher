namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ProtectedPathPolicy : IProtectedPathPolicy
{
    private readonly string[] protectedRoots;

    public ProtectedPathPolicy(IEnumerable<string> protectedRoots)
    {
        ArgumentNullException.ThrowIfNull(protectedRoots);
        this.protectedRoots = protectedRoots
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (this.protectedRoots.Length == 0)
        {
            throw new ArgumentException("At least one protected root is required.", nameof(protectedRoots));
        }
    }

    public IReadOnlyList<string> ProtectedRoots => protectedRoots;

    public static ProtectedPathPolicy FromLayout(CodexVsCodeStorageLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new ProtectedPathPolicy(
        [
            layout.ProtectedMainCodexDirectory,
            layout.ProtectedOriginalApplicationDataDirectory,
        ]);
    }

    public bool IsProtected(string path)
    {
        string candidate = Normalize(path);
        return protectedRoots.Any(root => IsSameOrDescendant(candidate, root));
    }

    public void AssertCanWrite(string path)
    {
        string candidate = Normalize(path);
        RejectReparsePointChain(candidate);
        if (protectedRoots.Any(root => IsSameOrDescendant(candidate, root)))
        {
            throw new UnauthorizedAccessException("The requested path belongs to protected Codex or legacy application state.");
        }
    }

    public void AssertCanRead(string path)
    {
        string candidate = Normalize(path);
        RejectReparsePointChain(candidate);
        if (protectedRoots.Any(root => IsSameOrDescendant(candidate, root)))
        {
            throw new UnauthorizedAccessException("Reading protected Codex or legacy application state is forbidden.");
        }
    }

    public void AssertCanCopy(string sourcePath, string destinationPath)
    {
        string source = Normalize(sourcePath);
        string destination = Normalize(destinationPath);
        RejectReparsePointChain(source);
        RejectReparsePointChain(destination);
        if (protectedRoots.Any(root => IsSameOrDescendant(source, root) || IsSameOrDescendant(destination, root)))
        {
            throw new UnauthorizedAccessException("Copying from or into protected Codex or legacy application state is forbidden.");
        }
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)));
    }

    private static bool IsSameOrDescendant(string candidate, string root)
        => candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RejectReparsePointChain(string path)
    {
        string? current = path;
        while (!string.IsNullOrEmpty(current))
        {
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("Reparse-point paths are not accepted for protected file operations.");
            }

            current = Path.GetDirectoryName(current);
        }
    }
}
