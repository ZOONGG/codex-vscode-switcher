using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed record ProxyProbeResult(bool IsProxyConfigured);

public sealed record WebSocketProbeResult(bool ServerReached, int? HttpStatusCode = null);

public interface INetworkDiagnosticProbe
{
    ProxyProbeResult DetectSystemProxy(Uri endpoint);
    Task ResolveDnsAsync(Uri endpoint, CancellationToken cancellationToken);
    Task ConnectTcpAsync(Uri endpoint, CancellationToken cancellationToken);
    Task NegotiateTlsAsync(Uri endpoint, string? customCaPath, CancellationToken cancellationToken);
    Task<int> GetHttpsStatusAsync(Uri endpoint, string? customCaPath, CancellationToken cancellationToken);
    Task<WebSocketProbeResult> ProbeWebSocketAsync(Uri endpoint, CancellationToken cancellationToken);
    Task StartBackendAsync(
        string executablePath,
        IReadOnlyDictionary<string, string> environmentOverrides,
        CancellationToken cancellationToken);
}

public sealed class CodexNetworkDiagnosticService
{
    public static readonly Uri DefaultHttpsEndpoint = new("https://chatgpt.com/backend-api");
    public static readonly Uri DefaultWebSocketEndpoint = new("wss://ws.chatgpt.com");
    public static readonly IReadOnlyList<string> NetworkEnvironmentVariableNames =
    [
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "NO_PROXY",
        "CODEX_CA_CERTIFICATE",
        "SSL_CERT_FILE",
        "SSL_CERT_DIR",
        "NODE_EXTRA_CA_CERTS",
        "REQUESTS_CA_BUNDLE",
        "CURL_CA_BUNDLE",
    ];

    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(12);
    private readonly INetworkDiagnosticProbe probe;

    public CodexNetworkDiagnosticService(INetworkDiagnosticProbe probe)
        => this.probe = probe;

    public async Task<CodexNetworkDiagnosticReport> RunAsync(
        CodexNetworkDiagnosticContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var results = new List<NetworkDiagnosticStageResult>();
        ProxyProbeResult proxy = probe.DetectSystemProxy(context.HttpsEndpoint);
        results.Add(new NetworkDiagnosticStageResult(
            NetworkDiagnosticStage.SystemProxy,
            NetworkDiagnosticStatus.Passed,
            TimeSpan.Zero));

        results.Add(CheckCertificate(context));
        results.Add(await RunStageAsync(
            NetworkDiagnosticStage.Dns,
            token => probe.ResolveDnsAsync(context.HttpsEndpoint, token),
            proxy.IsProxyConfigured,
            cancellationToken).ConfigureAwait(false));
        results.Add(await RunStageAsync(
            NetworkDiagnosticStage.Tcp,
            token => probe.ConnectTcpAsync(context.HttpsEndpoint, token),
            proxy.IsProxyConfigured,
            cancellationToken).ConfigureAwait(false));
        results.Add(await RunStageAsync(
            NetworkDiagnosticStage.Tls,
            token => probe.NegotiateTlsAsync(
                context.HttpsEndpoint,
                NormalizeCustomCa(context),
                token),
            proxy.IsProxyConfigured,
            cancellationToken).ConfigureAwait(false));

        results.Add(await RunHttpsAsync(context, proxy.IsProxyConfigured, cancellationToken).ConfigureAwait(false));
        results.Add(await RunWebSocketAsync(context, cancellationToken).ConfigureAwait(false));
        results.Add(await RunBackendAsync(context, cancellationToken).ConfigureAwait(false));

        IReadOnlyList<string> presentVariables = NetworkEnvironmentVariableNames
            .Where(name => Environment.GetEnvironmentVariable(name) is not null
                || context.BackendEnvironmentOverrides.ContainsKey(name))
            .ToArray();
        return new CodexNetworkDiagnosticReport(
            DateTimeOffset.UtcNow,
            context,
            proxy.IsProxyConfigured,
            presentVariables,
            results);
    }

    private static NetworkDiagnosticStageResult CheckCertificate(CodexNetworkDiagnosticContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool valid = context.CustomCaVariable == CustomCaEnvironmentVariable.None
            || (!string.IsNullOrWhiteSpace(context.CustomCaCertificatePath)
                && File.Exists(context.CustomCaCertificatePath));
        return new NetworkDiagnosticStageResult(
            NetworkDiagnosticStage.Certificate,
            valid ? NetworkDiagnosticStatus.Passed : NetworkDiagnosticStatus.Failed,
            stopwatch.Elapsed,
            valid ? NetworkFailureCategory.None : NetworkFailureCategory.CertificateMissing,
            valid ? null : "Configured certificate file is unavailable.");
    }

    private async Task<NetworkDiagnosticStageResult> RunHttpsAsync(
        CodexNetworkDiagnosticContext context,
        bool proxyConfigured,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            int status = await probe.GetHttpsStatusAsync(
                context.HttpsEndpoint,
                NormalizeCustomCa(context),
                timeout.Token).ConfigureAwait(false);
            NetworkFailureCategory category = status is 401 or 403
                ? NetworkFailureCategory.AuthenticationFailedAfterNetworkConnection
                : NetworkFailureCategory.None;
            return new NetworkDiagnosticStageResult(
                NetworkDiagnosticStage.Https,
                NetworkDiagnosticStatus.Passed,
                stopwatch.Elapsed,
                category,
                HttpStatusCode: status);
        }
        catch (Exception exception) when (IsDiagnosticException(exception, cancellationToken))
        {
            return Failure(
                NetworkDiagnosticStage.Https,
                stopwatch.Elapsed,
                exception,
                proxyConfigured);
        }
    }

    private async Task<NetworkDiagnosticStageResult> RunWebSocketAsync(
        CodexNetworkDiagnosticContext context,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            WebSocketProbeResult result = await probe.ProbeWebSocketAsync(
                context.WebSocketEndpoint,
                timeout.Token).ConfigureAwait(false);
            return new NetworkDiagnosticStageResult(
                NetworkDiagnosticStage.SecureWebSocket,
                result.ServerReached ? NetworkDiagnosticStatus.Passed : NetworkDiagnosticStatus.Warning,
                stopwatch.Elapsed,
                result.ServerReached
                    ? NetworkFailureCategory.None
                    : NetworkFailureCategory.WebSocketFailed,
                HttpStatusCode: result.HttpStatusCode);
        }
        catch (Exception exception) when (IsDiagnosticException(exception, cancellationToken))
        {
            return new NetworkDiagnosticStageResult(
                NetworkDiagnosticStage.SecureWebSocket,
                NetworkDiagnosticStatus.Warning,
                stopwatch.Elapsed,
                NetworkFailureCategory.WebSocketFailed,
                DiagnosticTextSanitizer.Sanitize(exception.Message));
        }
    }

    private async Task<NetworkDiagnosticStageResult> RunBackendAsync(
        CodexNetworkDiagnosticContext context,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(context.BackendExecutablePath)
            || !File.Exists(context.BackendExecutablePath))
        {
            return new NetworkDiagnosticStageResult(
                NetworkDiagnosticStage.BackendStart,
                NetworkDiagnosticStatus.Failed,
                stopwatch.Elapsed,
                NetworkFailureCategory.BackendExecutableMissing,
                "Codex backend executable was not found.");
        }

        try
        {
            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            await probe.StartBackendAsync(
                context.BackendExecutablePath,
                context.BackendEnvironmentOverrides,
                timeout.Token).ConfigureAwait(false);
            return new NetworkDiagnosticStageResult(
                NetworkDiagnosticStage.BackendStart,
                NetworkDiagnosticStatus.Passed,
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (IsDiagnosticException(exception, cancellationToken))
        {
            return new NetworkDiagnosticStageResult(
                NetworkDiagnosticStage.BackendStart,
                NetworkDiagnosticStatus.Failed,
                stopwatch.Elapsed,
                NetworkFailureCategory.BackendStartFailed,
                DiagnosticTextSanitizer.Sanitize(exception.Message));
        }
    }

    private async Task<NetworkDiagnosticStageResult> RunStageAsync(
        NetworkDiagnosticStage stage,
        Func<CancellationToken, Task> action,
        bool proxyConfigured,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            await action(timeout.Token).ConfigureAwait(false);
            return new NetworkDiagnosticStageResult(stage, NetworkDiagnosticStatus.Passed, stopwatch.Elapsed);
        }
        catch (Exception exception) when (IsDiagnosticException(exception, cancellationToken))
        {
            return Failure(stage, stopwatch.Elapsed, exception, proxyConfigured);
        }
    }

    private static NetworkDiagnosticStageResult Failure(
        NetworkDiagnosticStage stage,
        TimeSpan elapsed,
        Exception exception,
        bool proxyConfigured)
        => new(
            stage,
            NetworkDiagnosticStatus.Failed,
            elapsed,
            Classify(stage, exception, proxyConfigured),
            DiagnosticTextSanitizer.Sanitize(exception.Message));

    public static NetworkFailureCategory Classify(
        NetworkDiagnosticStage stage,
        Exception exception,
        bool proxyConfigured)
    {
        if (exception is OperationCanceledException or TimeoutException)
        {
            return NetworkFailureCategory.ConnectionTimedOut;
        }

        if (stage == NetworkDiagnosticStage.Dns)
        {
            return NetworkFailureCategory.DnsFailed;
        }

        if (exception is AuthenticationException)
        {
            return NetworkFailureCategory.TlsCertificateRejected;
        }

        if (exception is SocketException socket)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => NetworkFailureCategory.ConnectionRefused,
                SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
                    NetworkFailureCategory.NetworkUnreachable,
                SocketError.TimedOut => NetworkFailureCategory.ConnectionTimedOut,
                _ => proxyConfigured
                    ? NetworkFailureCategory.ProxyConnectionFailed
                    : NetworkFailureCategory.HttpsFailed,
            };
        }

        if (proxyConfigured && exception is HttpRequestException)
        {
            return NetworkFailureCategory.ProxyConnectionFailed;
        }

        return stage == NetworkDiagnosticStage.Tls
            ? NetworkFailureCategory.TlsCertificateRejected
            : NetworkFailureCategory.HttpsFailed;
    }

    private static string? NormalizeCustomCa(CodexNetworkDiagnosticContext context)
        => context.CustomCaVariable == CustomCaEnvironmentVariable.None
            ? null
            : context.CustomCaCertificatePath;

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(StageTimeout);
        return source;
    }

    private static bool IsDiagnosticException(Exception exception, CancellationToken outerToken)
    {
        if (exception is OperationCanceledException && outerToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is SocketException
            or HttpRequestException
            or AuthenticationException
            or WebSocketException
            or OperationCanceledException
            or TimeoutException
            or IOException
            or InvalidOperationException
            or Win32Exception;
    }
}

public sealed class SystemNetworkDiagnosticProbe : INetworkDiagnosticProbe
{
    public ProxyProbeResult DetectSystemProxy(Uri endpoint)
    {
        Uri? proxy = HttpClient.DefaultProxy.GetProxy(endpoint);
        return new ProxyProbeResult(proxy is not null && proxy != endpoint);
    }

    public async Task ResolveDnsAsync(Uri endpoint, CancellationToken cancellationToken)
        => _ = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);

    public async Task ConnectTcpAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Host, ResolvePort(endpoint), cancellationToken).ConfigureAwait(false);
    }

    public async Task NegotiateTlsAsync(
        Uri endpoint,
        string? customCaPath,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Host, ResolvePort(endpoint), cancellationToken).ConfigureAwait(false);
        using var ssl = new SslStream(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, chain, errors) =>
                CustomCertificateValidator.Validate(certificate, chain, errors, customCaPath));
        await ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = endpoint.Host },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetHttpsStatusAsync(
        Uri endpoint,
        string? customCaPath,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { UseProxy = true };
        if (!string.IsNullOrWhiteSpace(customCaPath))
        {
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) =>
                CustomCertificateValidator.Validate(certificate, chain, errors, customCaPath);
        }

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.ParseAdd("CodexVsCodeSwitcher-NetworkDiagnostic/1.0");
        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        return (int)response.StatusCode;
    }

    public async Task<WebSocketProbeResult> ProbeWebSocketAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new WebSocketProbeResult(true, 101);
        }
        catch (WebSocketException exception) when (TryReadHttpStatusCode(exception.Message, out _))
        {
            _ = TryReadHttpStatusCode(exception.Message, out int statusCode);
            return new WebSocketProbeResult(true, statusCode);
        }
    }

    public async Task StartBackendAsync(
        string executablePath,
        IReadOnlyDictionary<string, string> environmentOverrides,
        CancellationToken cancellationToken)
    {
        var spec = new VsCodeProcessStartSpec(
            Path.GetFullPath(executablePath),
            ["--version"],
            environmentOverrides);
        ProcessStartInfo startInfo = ManagedProcessStartInfoFactory.Create(spec);
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Codex backend executable did not start.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Codex backend executable returned a non-zero exit code.");
        }
    }

    private static int ResolvePort(Uri endpoint)
        => endpoint.IsDefaultPort ? 443 : endpoint.Port;

    private static bool TryReadHttpStatusCode(string message, out int statusCode)
    {
        statusCode = 0;
        Match match = Regex.Match(message, @"(?<!\d)(?<status>[1-5]\d{2})(?!\d)", RegexOptions.CultureInvariant);
        return match.Success
            && int.TryParse(match.Groups["status"].Value, out statusCode);
    }
}

internal static class CustomCertificateValidator
{
    public static bool Validate(
        X509Certificate? certificate,
        X509Chain? _,
        SslPolicyErrors errors,
        string? customCaPath)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (certificate is null || string.IsNullOrWhiteSpace(customCaPath) || !File.Exists(customCaPath))
        {
            return false;
        }

        using X509Certificate2 server = new(certificate);
        using X509Certificate2 customRoot = LoadCertificate(customCaPath);
        using var customChain = new X509Chain();
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        customChain.ChainPolicy.CustomTrustStore.Add(customRoot);
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return customChain.Build(server);
    }

    private static X509Certificate2 LoadCertificate(string path)
    {
        try
        {
            return X509Certificate2.CreateFromPemFile(path);
        }
        catch (CryptographicException)
        {
            return new X509Certificate2(File.ReadAllBytes(path));
        }
    }
}
