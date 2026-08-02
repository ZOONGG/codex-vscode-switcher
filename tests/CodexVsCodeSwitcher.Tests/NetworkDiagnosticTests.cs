using System.Net.Sockets;
using System.Security.Authentication;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class NetworkDiagnosticTests
{
    [Fact]
    public async Task Diagnostics_DistinguishDnsTcpTlsAndHttpsFailures()
    {
        using var temp = new TempDirectory();
        string backend = Path.Combine(temp.Path, "codex.exe");
        File.WriteAllText(backend, "test");
        var probe = new FakeProbe
        {
            DnsException = new SocketException((int)SocketError.HostNotFound),
            TcpException = new SocketException((int)SocketError.ConnectionRefused),
            TlsException = new AuthenticationException("certificate rejected"),
            HttpsException = new HttpRequestException("request failed"),
        };

        CodexNetworkDiagnosticReport report = await new CodexNetworkDiagnosticService(probe)
            .RunAsync(Context(temp.Path, backend), CancellationToken.None);

        Assert.Equal(NetworkFailureCategory.DnsFailed, Failure(report, NetworkDiagnosticStage.Dns));
        Assert.Equal(NetworkFailureCategory.ConnectionRefused, Failure(report, NetworkDiagnosticStage.Tcp));
        Assert.Equal(NetworkFailureCategory.TlsCertificateRejected, Failure(report, NetworkDiagnosticStage.Tls));
        Assert.Equal(NetworkFailureCategory.HttpsFailed, Failure(report, NetworkDiagnosticStage.Https));
    }

    [Fact]
    public async Task Http401_CountsAsSuccessfulConnectivityWithAuthenticationCategory()
    {
        using var temp = new TempDirectory();
        string backend = Path.Combine(temp.Path, "codex.exe");
        File.WriteAllText(backend, "test");
        var probe = new FakeProbe { HttpStatusCode = 401 };

        CodexNetworkDiagnosticReport report = await new CodexNetworkDiagnosticService(probe)
            .RunAsync(Context(temp.Path, backend), CancellationToken.None);
        NetworkDiagnosticStageResult https = Assert.Single(
            report.Stages,
            stage => stage.Stage == NetworkDiagnosticStage.Https);

        Assert.Equal(NetworkDiagnosticStatus.Passed, https.Status);
        Assert.Equal(401, https.HttpStatusCode);
        Assert.Equal(
            NetworkFailureCategory.AuthenticationFailedAfterNetworkConnection,
            https.FailureCategory);
        Assert.True(report.HttpsReachedServer);
    }

    [Fact]
    public void Classifier_DoesNotBypassTlsCertificateErrors()
    {
        NetworkFailureCategory category = CodexNetworkDiagnosticService.Classify(
            NetworkDiagnosticStage.Tls,
            new AuthenticationException("untrusted certificate"),
            proxyConfigured: false);

        Assert.Equal(NetworkFailureCategory.TlsCertificateRejected, category);
    }

    [Fact]
    public async Task FormattedReport_ContainsEnvironmentNamesButNeverValuesOrCommandLines()
    {
        using var temp = new TempDirectory();
        string backend = Path.Combine(temp.Path, "codex.exe");
        File.WriteAllText(backend, "test");
        var context = Context(temp.Path, backend) with
        {
            BackendEnvironmentOverrides = new Dictionary<string, string>
            {
                ["CODEX_HOME"] = Path.Combine(temp.Path, "profile"),
                ["HTTPS_PROXY"] = "http://name:secret-value@proxy.example.test",
            },
        };

        CodexNetworkDiagnosticReport report = await new CodexNetworkDiagnosticService(new FakeProbe())
            .RunAsync(context, CancellationToken.None);
        string formatted = CodexNetworkDiagnosticFormatter.Format(report, LanguagePreference.English);

        Assert.Contains("HTTPS_PROXY", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("--version", formatted, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(backend), formatted, StringComparison.Ordinal);
    }

    private static NetworkFailureCategory Failure(
        CodexNetworkDiagnosticReport report,
        NetworkDiagnosticStage stage)
        => Assert.Single(report.Stages, item => item.Stage == stage).FailureCategory;

    private static CodexNetworkDiagnosticContext Context(string root, string backend)
        => new(
            CodexNetworkDiagnosticService.DefaultHttpsEndpoint,
            CodexNetworkDiagnosticService.DefaultWebSocketEndpoint,
            VsCodeExtensionMode.Shared,
            Path.Combine(root, "extensions"),
            Path.Combine(root, "extensions", "openai.chatgpt-test"),
            backend,
            CustomCaEnvironmentVariable.None,
            null,
            new Dictionary<string, string>
            {
                ["CODEX_HOME"] = Path.Combine(root, "diagnostic-home"),
            });

    private sealed class FakeProbe : INetworkDiagnosticProbe
    {
        public Exception? DnsException { get; init; }
        public Exception? TcpException { get; init; }
        public Exception? TlsException { get; init; }
        public Exception? HttpsException { get; init; }
        public int HttpStatusCode { get; init; } = 204;
        public bool ProxyConfigured { get; init; }

        public ProxyProbeResult DetectSystemProxy(Uri endpoint) => new(ProxyConfigured);

        public Task ResolveDnsAsync(Uri endpoint, CancellationToken cancellationToken)
            => Result(DnsException);

        public Task ConnectTcpAsync(Uri endpoint, CancellationToken cancellationToken)
            => Result(TcpException);

        public Task NegotiateTlsAsync(
            Uri endpoint,
            string? customCaPath,
            CancellationToken cancellationToken)
            => Result(TlsException);

        public Task<int> GetHttpsStatusAsync(
            Uri endpoint,
            string? customCaPath,
            CancellationToken cancellationToken)
            => HttpsException is null
                ? Task.FromResult(HttpStatusCode)
                : Task.FromException<int>(HttpsException);

        public Task<WebSocketProbeResult> ProbeWebSocketAsync(
            Uri endpoint,
            CancellationToken cancellationToken)
            => Task.FromResult(new WebSocketProbeResult(true, 401));

        public Task StartBackendAsync(
            string executablePath,
            IReadOnlyDictionary<string, string> environmentOverrides,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        private static Task Result(Exception? exception)
            => exception is null ? Task.CompletedTask : Task.FromException(exception);
    }
}
