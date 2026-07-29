namespace CodexVsCodeSwitcher.Core.Services;

public sealed class VsCodeExecutableLocator : IVsCodeExecutableLocator
{
    private readonly string localAppData;
    private readonly string programFiles;
    private readonly string programFilesX86;

    public VsCodeExecutableLocator(string localAppData, string programFiles, string programFilesX86)
    {
        this.localAppData = NormalizeOptionalRoot(localAppData);
        this.programFiles = NormalizeOptionalRoot(programFiles);
        this.programFilesX86 = NormalizeOptionalRoot(programFilesX86);
    }

    public static VsCodeExecutableLocator FromEnvironment()
        => new(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

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
    }

    private static string NormalizeOptionalRoot(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
}
