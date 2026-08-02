using System.Text.Json;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ProxyAndCertificateSettingsTests
{
    [Theory]
    [InlineData(CustomCaEnvironmentVariable.CodexCaCertificate, "CODEX_CA_CERTIFICATE")]
    [InlineData(CustomCaEnvironmentVariable.SslCertFile, "SSL_CERT_FILE")]
    public void CustomCaPath_IsValidatedAndAddedOnlyToTheManagedEnvironment(
        CustomCaEnvironmentVariable mode,
        string expectedVariable)
    {
        using var temp = new TempDirectory();
        string certificate = Path.Combine(temp.Path, "corporate-ca.pem");
        File.WriteAllText(certificate, "dummy-test-certificate");
        string? parentBefore = Environment.GetEnvironmentVariable(expectedVariable);

        IReadOnlyDictionary<string, string> overrides =
            ManagedEnvironmentOverridesBuilder.Build(
                Path.Combine(temp.Path, "codex-home"),
                mode,
                certificate);

        Assert.Equal(2, overrides.Count);
        Assert.Equal(Path.GetFullPath(certificate), overrides[expectedVariable]);
        Assert.Equal(parentBefore, Environment.GetEnvironmentVariable(expectedVariable));
    }

    [Fact]
    public void MissingCustomCaPath_IsRejectedWithoutDisablingTlsValidation()
    {
        using var temp = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() =>
            ManagedEnvironmentOverridesBuilder.Build(
                Path.Combine(temp.Path, "codex-home"),
                CustomCaEnvironmentVariable.CodexCaCertificate,
                Path.Combine(temp.Path, "missing.pem")));
    }

    [Fact]
    public void ProxyPlan_CopiesOnlySafeAllowlistedSettings()
    {
        using var layout = new TestLayout();
        string source = Path.Combine(layout.LocalAppData, "ordinary-settings.json");
        Directory.CreateDirectory(layout.LocalAppData);
        File.WriteAllText(source, """
            {
              "http.proxy": "http://proxy.example.test:8080",
              "http.proxyStrictSSL": true,
              "http.proxySupport": "override",
              "http.proxyAuthorization": "Basic secret-test-value",
              "authorization": "secret-test-value",
              "editor.fontSize": 18
            }
            """);
        var service = new VsCodeProxySettingsService(layout.ProtectedPaths);

        VsCodeProxySettingsPlan plan = service.BuildPlan(source);

        Assert.Equal(
            new[] { "http.proxy", "http.proxyStrictSSL", "http.proxySupport" },
            plan.SettingNames);
        Assert.DoesNotContain("http.proxyAuthorization", plan.Settings.Keys);
        Assert.DoesNotContain("authorization", plan.Settings.Keys);
    }

    [Fact]
    public void ProxyPlan_RejectsCredentialsEmbeddedInProxyUrl()
    {
        using var layout = new TestLayout();
        string source = Path.Combine(layout.LocalAppData, "ordinary-settings.json");
        Directory.CreateDirectory(layout.LocalAppData);
        File.WriteAllText(source, "{\"http.proxy\":\"http://name:password@proxy.example.test:8080\"}");
        var service = new VsCodeProxySettingsService(layout.ProtectedPaths);

        VsCodeProxySettingsPlan plan = service.BuildPlan(source);

        Assert.Empty(plan.Settings);
    }

    [Fact]
    public void ProxyApply_ModifiesOnlyDedicatedSettingsAndPreservesOrdinaryFile()
    {
        using var layout = new TestLayout();
        string source = Path.Combine(layout.LocalAppData, "ordinary-settings.json");
        Directory.CreateDirectory(layout.LocalAppData);
        const string ordinaryContents = "{\"http.proxyStrictSSL\":true}";
        File.WriteAllText(source, ordinaryContents);
        var service = new VsCodeProxySettingsService(layout.ProtectedPaths);
        VsCodeProxySettingsPlan plan = service.BuildPlan(source);

        service.Apply(layout.Paths.VsCodeUserDataDirectory, plan);

        Assert.Equal(ordinaryContents, File.ReadAllText(source));
        string destination = Path.Combine(
            layout.Paths.VsCodeUserDataDirectory,
            "User",
            "settings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(destination));
        Assert.True(document.RootElement.GetProperty("http.proxyStrictSSL").GetBoolean());
    }
}
