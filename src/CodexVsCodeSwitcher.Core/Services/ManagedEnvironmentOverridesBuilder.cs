using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public static class ManagedEnvironmentOverridesBuilder
{
    public static IReadOnlyDictionary<string, string> Build(
        string codexHome,
        CustomCaEnvironmentVariable customCaVariable,
        string? customCaCertificatePath)
    {
        string normalizedCodexHome = Path.GetFullPath(codexHome);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [VsCodeLaunchPlanBuilder.CodexHomeVariable] = normalizedCodexHome,
        };
        if (customCaVariable == CustomCaEnvironmentVariable.None)
        {
            return overrides;
        }

        if (string.IsNullOrWhiteSpace(customCaCertificatePath))
        {
            throw new FileNotFoundException("The configured custom CA certificate file does not exist.");
        }

        string certificatePath = Path.GetFullPath(customCaCertificatePath.Trim());
        if (!File.Exists(certificatePath))
        {
            throw new FileNotFoundException(
                "The configured custom CA certificate file does not exist.",
                certificatePath);
        }

        string variableName = customCaVariable switch
        {
            CustomCaEnvironmentVariable.CodexCaCertificate => "CODEX_CA_CERTIFICATE",
            CustomCaEnvironmentVariable.SslCertFile => "SSL_CERT_FILE",
            _ => throw new ArgumentOutOfRangeException(nameof(customCaVariable)),
        };
        overrides[variableName] = certificatePath;
        return overrides;
    }
}
