using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class CodexExtensionInstallationLocatorTests
{
    [Fact]
    public void Locate_AcceptsExactOfficialManifest()
    {
        using var layout = new TestLayout();
        string extension = WriteExtension(
            layout.Paths.VsCodeSharedExtensionsDirectory,
            "openai",
            "chatgpt",
            "1.2.3");
        var locator = new CodexExtensionInstallationLocator(layout.ProtectedPaths);

        CodexExtensionInstallationInfo? result = locator.Locate(
            layout.Paths.VsCodeSharedExtensionsDirectory);

        Assert.NotNull(result);
        Assert.Equal(extension, result.ExtensionPath);
        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public void Locate_RejectsLookalikeDirectoryWithDifferentPublisher()
    {
        using var layout = new TestLayout();
        _ = WriteExtension(
            layout.Paths.VsCodeSharedExtensionsDirectory,
            "not-openai",
            "chatgpt",
            "1.2.3");
        var locator = new CodexExtensionInstallationLocator(layout.ProtectedPaths);

        Assert.Null(locator.Locate(layout.Paths.VsCodeSharedExtensionsDirectory));
    }

    private static string WriteExtension(
        string root,
        string publisher,
        string name,
        string version)
    {
        string extension = Path.Combine(root, "openai.chatgpt-1.2.3-win32-x64");
        Directory.CreateDirectory(extension);
        File.WriteAllText(
            Path.Combine(extension, "package.json"),
            $$"""
            {
              "publisher": "{{publisher}}",
              "name": "{{name}}",
              "version": "{{version}}"
            }
            """);
        return extension;
    }
}
