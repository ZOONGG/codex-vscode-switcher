namespace CodexVsCodeSwitcher.Core.Models;

public enum NetworkDiagnosticStage
{
    SystemProxy,
    Certificate,
    Dns,
    Tcp,
    Tls,
    Https,
    SecureWebSocket,
    BackendStart,
}

public enum NetworkDiagnosticStatus
{
    Passed,
    Warning,
    Failed,
}

public enum NetworkFailureCategory
{
    None,
    DnsFailed,
    ConnectionTimedOut,
    ConnectionRefused,
    NetworkUnreachable,
    TlsCertificateRejected,
    ProxyConnectionFailed,
    HttpsFailed,
    WebSocketFailed,
    CertificateMissing,
    BackendExecutableMissing,
    BackendStartFailed,
    AuthenticationFailedAfterNetworkConnection,
}

public sealed record NetworkDiagnosticStageResult(
    NetworkDiagnosticStage Stage,
    NetworkDiagnosticStatus Status,
    TimeSpan Elapsed,
    NetworkFailureCategory FailureCategory = NetworkFailureCategory.None,
    string? SanitizedError = null,
    int? HttpStatusCode = null);

public sealed record CodexNetworkDiagnosticContext(
    Uri HttpsEndpoint,
    Uri WebSocketEndpoint,
    VsCodeExtensionMode ExtensionMode,
    string ExtensionsDirectory,
    string? ExtensionPath,
    string? BackendExecutablePath,
    CustomCaEnvironmentVariable CustomCaVariable,
    string? CustomCaCertificatePath,
    IReadOnlyDictionary<string, string> BackendEnvironmentOverrides,
    IReadOnlyList<BackendProcessPathDiagnostic>? ManagedProcesses = null);

public sealed record BackendProcessPathDiagnostic(
    string ProcessName,
    string ExecutablePath,
    int ParentProcessId,
    DateTimeOffset StartTimeUtc,
    string? ExtensionDirectory);

public sealed record CodexNetworkDiagnosticReport(
    DateTimeOffset TimestampUtc,
    CodexNetworkDiagnosticContext Context,
    bool SystemProxyConfigured,
    IReadOnlyList<string> PresentNetworkEnvironmentVariables,
    IReadOnlyList<NetworkDiagnosticStageResult> Stages)
{
    public bool HttpsReachedServer => Stages.Any(stage =>
        stage.Stage == NetworkDiagnosticStage.Https
        && stage.Status != NetworkDiagnosticStatus.Failed);

    public NetworkDiagnosticStageResult? FirstFailure =>
        Stages.FirstOrDefault(stage => stage.Status == NetworkDiagnosticStatus.Failed);
}
