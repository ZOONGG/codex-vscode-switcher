using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed record VsCodeEnvironmentSnapshot(
    string ExecutablePath,
    string UserDataDirectory,
    string ExtensionsDirectory,
    string? CodexExtensionVersion,
    string? CodexExtensionPath,
    string? BackendExecutablePath,
    string? BackendSha256,
    IReadOnlyList<string> SafeProxySettingNames);

public sealed record VsCodeEnvironmentComparison(
    VsCodeEnvironmentSnapshot Ordinary,
    VsCodeEnvironmentSnapshot Managed,
    VsCodeExtensionMode ManagedExtensionMode,
    string ProcessArchitecture,
    bool SystemProxyConfigured,
    IReadOnlyList<string> PresentNetworkEnvironmentVariables,
    string? VsCodeExecutableSha256)
{
    public bool BackendPathsDiffer => !string.Equals(
        Ordinary.BackendExecutablePath,
        Managed.BackendExecutablePath,
        StringComparison.OrdinalIgnoreCase);
}

public sealed class VsCodeEnvironmentComparisonService
{
    private readonly CodexExtensionInstallationLocator extensionLocator;
    private readonly VsCodeProxySettingsService proxySettingsService;

    public VsCodeEnvironmentComparisonService(IProtectedPathPolicy protectedPaths)
    {
        extensionLocator = new CodexExtensionInstallationLocator(protectedPaths);
        proxySettingsService = new VsCodeProxySettingsService(protectedPaths);
    }

    public VsCodeEnvironmentComparison Build(
        string executablePath,
        string ordinaryUserDataDirectory,
        string managedUserDataDirectory,
        string ordinaryExtensionsDirectory,
        string managedExtensionsDirectory,
        VsCodeExtensionMode managedExtensionMode)
    {
        string executable = Path.GetFullPath(executablePath);
        VsCodeEnvironmentSnapshot ordinary = Snapshot(
            executable,
            ordinaryUserDataDirectory,
            ordinaryExtensionsDirectory);
        VsCodeEnvironmentSnapshot managed = Snapshot(
            executable,
            managedUserDataDirectory,
            managedExtensionsDirectory);
        Uri endpoint = CodexNetworkDiagnosticService.DefaultHttpsEndpoint;
        Uri? proxy = HttpClient.DefaultProxy.GetProxy(endpoint);
        return new VsCodeEnvironmentComparison(
            ordinary,
            managed,
            managedExtensionMode,
            RuntimeInformation.ProcessArchitecture.ToString(),
            proxy is not null && proxy != endpoint,
            CodexNetworkDiagnosticService.NetworkEnvironmentVariableNames
                .Where(name => Environment.GetEnvironmentVariable(name) is not null)
                .ToArray(),
            Hash(executable));
    }

    private VsCodeEnvironmentSnapshot Snapshot(
        string executablePath,
        string userDataDirectory,
        string extensionsDirectory)
    {
        string userData = Path.GetFullPath(userDataDirectory);
        string extensions = Path.GetFullPath(extensionsDirectory);
        CodexExtensionInstallationInfo? extension = extensionLocator.Locate(extensions);
        string settingsFile = Path.Combine(userData, "User", "settings.json");
        IReadOnlyList<string> settingNames = proxySettingsService.BuildPlan(settingsFile).SettingNames;
        return new VsCodeEnvironmentSnapshot(
            executablePath,
            userData,
            extensions,
            extension?.Version,
            extension?.ExtensionPath,
            extension?.BackendExecutablePath,
            Hash(extension?.BackendExecutablePath),
            settingNames);
    }

    private static string? Hash(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

public static class VsCodeEnvironmentComparisonFormatter
{
    public static string Format(VsCodeEnvironmentComparison comparison, LanguagePreference language)
    {
        var output = new StringBuilder();
        Add(output, LocalizationCatalog.Text(language, "ComparisonArchitecture"), comparison.ProcessArchitecture);
        Add(output, LocalizationCatalog.Text(language, "ComparisonSystemProxy"),
            LocalizationCatalog.Text(language,
                comparison.SystemProxyConfigured ? "SystemProxyDetected" : "SystemProxyDirect"));
        Add(output, LocalizationCatalog.Text(language, "ComparisonEnvironmentNames"),
            comparison.PresentNetworkEnvironmentVariables.Count == 0
                ? "-"
                : string.Join(", ", comparison.PresentNetworkEnvironmentVariables));
        Add(output, LocalizationCatalog.Text(language, "ComparisonVsCodeHash"), comparison.VsCodeExecutableSha256 ?? "-");
        output.AppendLine();
        AppendSnapshot(output, LocalizationCatalog.Text(language, "ComparisonOrdinaryVsCode"), comparison.Ordinary, language);
        output.AppendLine();
        AppendSnapshot(output, LocalizationCatalog.Text(language, "ComparisonManagedVsCode"), comparison.Managed, language);
        output.AppendLine();
        Add(output, LocalizationCatalog.Text(language, "ComparisonBackendPathsDiffer"),
            LocalizationCatalog.Text(language, comparison.BackendPathsDiffer ? "Yes" : "No"));
        return output.ToString().TrimEnd();
    }

    private static void AppendSnapshot(
        StringBuilder output,
        string title,
        VsCodeEnvironmentSnapshot snapshot,
        LanguagePreference language)
    {
        output.AppendLine(title);
        Add(output, LocalizationCatalog.Text(language, "ComparisonExecutable"), snapshot.ExecutablePath);
        Add(output, LocalizationCatalog.Text(language, "ComparisonUserData"), snapshot.UserDataDirectory);
        Add(output, LocalizationCatalog.Text(language, "ComparisonExtensions"), snapshot.ExtensionsDirectory);
        Add(output, LocalizationCatalog.Text(language, "ComparisonExtensionVersion"), snapshot.CodexExtensionVersion ?? "-");
        Add(output, LocalizationCatalog.Text(language, "ComparisonExtensionPath"), snapshot.CodexExtensionPath ?? "-");
        Add(output, LocalizationCatalog.Text(language, "ComparisonBackendPath"), snapshot.BackendExecutablePath ?? "-");
        Add(output, LocalizationCatalog.Text(language, "ComparisonBackendHash"), snapshot.BackendSha256 ?? "-");
        Add(output, LocalizationCatalog.Text(language, "ComparisonProxySettingNames"),
            snapshot.SafeProxySettingNames.Count == 0 ? "-" : string.Join(", ", snapshot.SafeProxySettingNames));
    }

    private static void Add(StringBuilder output, string label, string value)
        => output.Append(label).Append(": ").AppendLine(value);
}
