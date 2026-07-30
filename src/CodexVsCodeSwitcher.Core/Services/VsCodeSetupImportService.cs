using System.Text.Json;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed record VsCodeSetupImportFile(
    string SourcePath,
    string DestinationRelativePath,
    long Length);

public sealed record VsCodeSetupImportPlan(
    string SourceUserDirectory,
    string SourceExtensionsDirectory,
    IReadOnlyList<VsCodeSetupImportFile> Files,
    IReadOnlyList<string> ExtensionIds);

public sealed record VsCodeExtensionImportFailure(
    string ExtensionId,
    string Reason);

public sealed record VsCodeSetupImportResult(
    IReadOnlyList<string> CopiedFiles,
    IReadOnlyList<string> InstalledExtensionIds,
    IReadOnlyList<VsCodeExtensionImportFailure> ExtensionFailures);

public sealed class VsCodeSetupImportService
{
    private const long MaximumFileBytes = 2 * 1024 * 1024;
    private const long MaximumTotalBytes = 20 * 1024 * 1024;
    private const int MaximumExtensions = 500;
    private readonly IProtectedPathPolicy protectedPaths;
    private readonly IProcessCommandRunner processRunner;
    private readonly VsCodeLaunchPlanBuilder launchPlanBuilder;

    public VsCodeSetupImportService(
        IProtectedPathPolicy protectedPaths,
        IProcessCommandRunner processRunner,
        VsCodeLaunchPlanBuilder launchPlanBuilder)
    {
        this.protectedPaths = protectedPaths;
        this.processRunner = processRunner;
        this.launchPlanBuilder = launchPlanBuilder;
    }

    public VsCodeSetupImportPlan BuildPlan(
        string ordinaryUserDirectory,
        string ordinaryExtensionsDirectory)
    {
        string userDirectory = Path.GetFullPath(ordinaryUserDirectory);
        string extensionsDirectory = Path.GetFullPath(ordinaryExtensionsDirectory);
        var files = new List<VsCodeSetupImportFile>();
        long totalBytes = 0;
        AddAllowedFile(userDirectory, "settings.json", "settings.json", files, ref totalBytes);
        AddAllowedFile(userDirectory, "keybindings.json", "keybindings.json", files, ref totalBytes);
        AddSnippetTree(
            Path.Combine(userDirectory, "snippets"),
            "snippets",
            files,
            ref totalBytes,
            depth: 0);
        AddNamedProfiles(userDirectory, files, ref totalBytes);

        IReadOnlyList<string> extensionIds = EnumerateExtensionIds(extensionsDirectory);
        return new VsCodeSetupImportPlan(
            userDirectory,
            extensionsDirectory,
            files.OrderBy(static file => file.DestinationRelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            extensionIds);
    }

    public async Task<VsCodeSetupImportResult> ImportAsync(
        VsCodeSetupImportPlan plan,
        string executablePath,
        string dedicatedUserDataDirectory,
        string dedicatedExtensionsDirectory,
        string dedicatedSharedDataDirectory,
        IReadOnlyCollection<string> selectedExtensionIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string destinationUser = Path.Combine(
            Path.GetFullPath(dedicatedUserDataDirectory),
            "User");
        protectedPaths.AssertCanWrite(destinationUser);
        Directory.CreateDirectory(destinationUser);
        var copied = new List<string>();
        foreach (VsCodeSetupImportFile file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyAllowedFile(file, destinationUser);
            copied.Add(file.DestinationRelativePath);
        }

        var installed = new List<string>();
        var failures = new List<VsCodeExtensionImportFailure>();
        var planned = new HashSet<string>(plan.ExtensionIds, StringComparer.OrdinalIgnoreCase);
        foreach (string requested in selectedExtensionIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!planned.Contains(requested)
                || requested.Equals(CodexExtensionManager.ExtensionId, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new VsCodeExtensionImportFailure(requested, "not-in-confirmed-plan"));
                continue;
            }

            try
            {
                VsCodeProcessStartSpec install = launchPlanBuilder.BuildExtensionInstall(
                    executablePath,
                    dedicatedUserDataDirectory,
                    dedicatedExtensionsDirectory,
                    dedicatedSharedDataDirectory,
                    requested);
                int exitCode = await processRunner.RunAsync(install, cancellationToken).ConfigureAwait(false);
                if (exitCode == 0)
                {
                    installed.Add(requested);
                }
                else
                {
                    failures.Add(new VsCodeExtensionImportFailure(
                        requested,
                        $"exit-code-{exitCode}"));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                failures.Add(new VsCodeExtensionImportFailure(
                    requested,
                    DiagnosticTextSanitizer.Sanitize(exception.Message) ?? exception.GetType().Name));
            }
        }

        return new VsCodeSetupImportResult(copied, installed, failures);
    }

    private void AddNamedProfiles(
        string userDirectory,
        List<VsCodeSetupImportFile> files,
        ref long totalBytes)
    {
        string profilesRoot = Path.Combine(userDirectory, "profiles");
        if (!IsSafeDirectory(profilesRoot))
        {
            return;
        }

        foreach (string profileDirectory in Directory.EnumerateDirectories(profilesRoot).Take(20))
        {
            if (!IsSafeDirectory(profileDirectory))
            {
                continue;
            }

            string profileName = Path.GetFileName(profileDirectory);
            string destinationRoot = Path.Combine("profiles", profileName);
            AddAllowedFile(
                profileDirectory,
                "settings.json",
                Path.Combine(destinationRoot, "settings.json"),
                files,
                ref totalBytes);
            AddAllowedFile(
                profileDirectory,
                "keybindings.json",
                Path.Combine(destinationRoot, "keybindings.json"),
                files,
                ref totalBytes);
            AddSnippetTree(
                Path.Combine(profileDirectory, "snippets"),
                Path.Combine(destinationRoot, "snippets"),
                files,
                ref totalBytes,
                depth: 0);
        }
    }

    private void AddSnippetTree(
        string sourceDirectory,
        string destinationRelativeDirectory,
        List<VsCodeSetupImportFile> files,
        ref long totalBytes,
        int depth)
    {
        if (depth > 4 || !IsSafeDirectory(sourceDirectory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string extension = Path.GetExtension(file);
            if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".code-snippets", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddAllowedFile(
                sourceDirectory,
                Path.GetFileName(file),
                Path.Combine(destinationRelativeDirectory, Path.GetFileName(file)),
                files,
                ref totalBytes);
        }

        foreach (string child in Directory.EnumerateDirectories(sourceDirectory))
        {
            AddSnippetTree(
                child,
                Path.Combine(destinationRelativeDirectory, Path.GetFileName(child)),
                files,
                ref totalBytes,
                depth + 1);
        }
    }

    private void AddAllowedFile(
        string sourceDirectory,
        string sourceName,
        string destinationRelativePath,
        List<VsCodeSetupImportFile> files,
        ref long totalBytes)
    {
        string source = Path.GetFullPath(Path.Combine(sourceDirectory, sourceName));
        if (!File.Exists(source)
            || File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
        {
            return;
        }

        protectedPaths.AssertCanRead(source);
        long length = new FileInfo(source).Length;
        if (length > MaximumFileBytes
            || totalBytes + length > MaximumTotalBytes)
        {
            return;
        }

        totalBytes += length;
        files.Add(new VsCodeSetupImportFile(source, destinationRelativePath, length));
    }

    private IReadOnlyList<string> EnumerateExtensionIds(string extensionsDirectory)
    {
        if (!IsSafeDirectory(extensionsDirectory))
        {
            return [];
        }

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in Directory.EnumerateDirectories(extensionsDirectory).Take(MaximumExtensions))
        {
            if (!IsSafeDirectory(directory))
            {
                continue;
            }

            string manifest = Path.Combine(directory, "package.json");
            if (!File.Exists(manifest)
                || new FileInfo(manifest).Length > MaximumFileBytes)
            {
                continue;
            }

            protectedPaths.AssertCanRead(manifest);
            try
            {
                using FileStream stream = File.OpenRead(manifest);
                using JsonDocument document = JsonDocument.Parse(stream);
                string? publisher = document.RootElement.TryGetProperty("publisher", out JsonElement publisherElement)
                    ? publisherElement.GetString()
                    : null;
                string? name = document.RootElement.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(publisher) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string identifier = $"{publisher}.{name}";
                _ = launchPlanBuilder.BuildExtensionInstall(
                    Path.GetFullPath("Code.exe"),
                    Path.GetFullPath("data"),
                    Path.GetFullPath("extensions"),
                    Path.GetFullPath("shared-data"),
                    identifier);
                if (!identifier.Equals(CodexExtensionManager.ExtensionId, StringComparison.OrdinalIgnoreCase))
                {
                    identifiers.Add(identifier);
                }
            }
            catch (Exception exception) when (
                exception is JsonException
                    or IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
            }
        }

        return identifiers.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void CopyAllowedFile(VsCodeSetupImportFile file, string destinationUser)
    {
        protectedPaths.AssertCanRead(file.SourcePath);
        string relative = file.DestinationRelativePath;
        if (Path.IsPathFullyQualified(relative))
        {
            throw new ArgumentException("Import destination must be relative.", nameof(file));
        }

        string destination = Path.GetFullPath(Path.Combine(destinationUser, relative));
        string expectedRoot = Path.TrimEndingDirectorySeparator(destinationUser)
            + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Import destination escaped the dedicated user directory.");
        }

        protectedPaths.AssertCanWrite(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + $".import-{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(file.SourcePath, temporary, overwrite: false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool IsSafeDirectory(string path)
        => Directory.Exists(path)
            && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
