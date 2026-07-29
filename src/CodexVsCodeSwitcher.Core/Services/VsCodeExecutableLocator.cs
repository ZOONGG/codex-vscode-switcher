namespace CodexVsCodeSwitcher.Core.Services;

public sealed class VsCodeExecutableLocator : IVsCodeExecutableLocator
{
    private readonly string localAppData;
    private readonly string programFiles;
    private readonly string programFilesX86;
    private readonly string pathEnvironment;

    public VsCodeExecutableLocator(
        string localAppData,
        string programFiles,
        string programFilesX86,
        string? pathEnvironment = null)
    {
        this.localAppData = NormalizeOptionalRoot(localAppData);
        this.programFiles = NormalizeOptionalRoot(programFiles);
        this.programFilesX86 = NormalizeOptionalRoot(programFilesX86);
        this.pathEnvironment = pathEnvironment ?? string.Empty;
    }

    public static VsCodeExecutableLocator FromEnvironment()
        => new(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("PATH"));

    public string? Locate(string? configuredExecutablePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredExecutablePath))
        {
            try
            {
                string configured = Path.GetFullPath(configuredExecutablePath.Trim());
                return IsSupportedExecutable(configured) && File.Exists(configured) ? configured : null;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        return CandidatePaths()
            .Where(static path => path.Length > 0)
            .FirstOrDefault(File.Exists);
    }

    public static bool IsSupportedExecutable(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Equals("Code.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<string> CandidatePaths()
    {
        if (localAppData.Length > 0)
        {
            yield return Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
            yield return Path.Combine(localAppData, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe");
        }

        if (programFiles.Length > 0)
        {
            yield return Path.Combine(programFiles, "Microsoft VS Code", "Code.exe");
            yield return Path.Combine(programFiles, "Microsoft VS Code Insiders", "Code - Insiders.exe");
        }

        if (programFilesX86.Length > 0)
        {
            yield return Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe");
            yield return Path.Combine(programFilesX86, "Microsoft VS Code Insiders", "Code - Insiders.exe");
        }

        foreach (string path in PathEnvironmentCandidates())
        {
            yield return path;
        }
    }

    private IEnumerable<string> PathEnvironmentCandidates()
    {
        foreach (string entry in pathEnvironment.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidateRoot = entry.Trim('"');
            if (!Path.IsPathFullyQualified(candidateRoot))
            {
                continue;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(candidateRoot);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
                continue;
            }

            string codeExecutable = Path.Combine(fullRoot, "Code.exe");
            if (File.Exists(codeExecutable))
            {
                yield return codeExecutable;
            }

            string insidersExecutable = Path.Combine(fullRoot, "Code - Insiders.exe");
            if (File.Exists(insidersExecutable))
            {
                yield return insidersExecutable;
            }

            foreach ((string ShimName, string ExecutableName) shim in new[]
            {
                ("code.cmd", "Code.exe"),
                ("code-insiders.cmd", "Code - Insiders.exe"),
            })
            {
                string shimPath = Path.Combine(fullRoot, shim.ShimName);
                if (!File.Exists(shimPath)
                    || !Path.GetFileName(fullRoot).Equals("bin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? installRoot = Directory.GetParent(fullRoot)?.FullName;
                if (installRoot is not null)
                {
                    yield return Path.Combine(installRoot, shim.ExecutableName);
                }
            }
        }
    }

    private static string NormalizeOptionalRoot(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
}
