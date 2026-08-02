param(
    [int]$TimeoutSeconds = 12
)

$ErrorActionPreference = "Stop"
$httpsEndpoint = [Uri]"https://chatgpt.com/backend-api"
$webSocketEndpoint = [Uri]"wss://ws.chatgpt.com"
$results = [System.Collections.Generic.List[object]]::new()
$networkEnvironmentNames = @(
    "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
    "CODEX_CA_CERTIFICATE", "SSL_CERT_FILE", "SSL_CERT_DIR",
    "NODE_EXTRA_CA_CERTS", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE"
)

function Protect-DiagnosticText {
    param([string]$Value)

    $safe = $Value.Replace($env:USERPROFILE, "%USERPROFILE%").Replace($env:LOCALAPPDATA, "%LOCALAPPDATA%")
    $safe = [regex]::Replace($safe, '(?i)Bearer\s+[A-Za-z0-9._~+/=-]+', 'Bearer <redacted>')
    $safe = [regex]::Replace($safe, '[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}', '<email-redacted>', 'IgnoreCase')
    if ($safe.Length -gt 300) {
        return $safe.Substring(0, 300) + "…"
    }

    return $safe
}

function Invoke-DiagnosticStage {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        $detail = & $Action
        $results.Add([pscustomobject]@{
            Stage = $Name
            Status = "Passed"
            ElapsedMs = [math]::Round($timer.Elapsed.TotalMilliseconds)
            Detail = $detail
        })
    }
    catch {
        $results.Add([pscustomobject]@{
            Stage = $Name
            Status = "Failed"
            ElapsedMs = [math]::Round($timer.Elapsed.TotalMilliseconds)
            Detail = Protect-DiagnosticText $_.Exception.Message
        })
    }
}

$proxy = [Net.Http.HttpClient]::DefaultProxy.GetProxy($httpsEndpoint)
$results.Add([pscustomobject]@{
    Stage = "SystemProxy"
    Status = "Passed"
    ElapsedMs = 0
    Detail = if ($null -ne $proxy -and $proxy -ne $httpsEndpoint) { "DetectedValueHidden" } else { "DirectOrSystemDefault" }
})

Invoke-DiagnosticStage "DNS" {
    $addresses = [Net.Dns]::GetHostAddressesAsync($httpsEndpoint.Host).GetAwaiter().GetResult()
    if ($addresses.Count -eq 0) { throw "DNS returned no addresses." }
    "Resolved"
}

Invoke-DiagnosticStage "TCP443" {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $cancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
        try {
            $client.ConnectAsync($httpsEndpoint.Host, 443, $cancellation.Token).AsTask().GetAwaiter().GetResult()
        }
        finally {
            $cancellation.Dispose()
        }
        "Connected"
    }
    finally {
        $client.Dispose()
    }
}

Invoke-DiagnosticStage "TLS" {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $client.Connect($httpsEndpoint.Host, 443)
        $ssl = [Net.Security.SslStream]::new($client.GetStream(), $false)
        try {
            $ssl.AuthenticateAsClient($httpsEndpoint.Host)
            "Negotiated"
        }
        finally {
            $ssl.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}

Invoke-DiagnosticStage "HTTPS" {
    $client = [Net.Http.HttpClient]::new()
    try {
        $cancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
        try {
            $response = $client.GetAsync($httpsEndpoint, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cancellation.Token).GetAwaiter().GetResult()
            try {
                "HTTP " + [int]$response.StatusCode
            }
            finally {
                $response.Dispose()
            }
        }
        finally {
            $cancellation.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}

Invoke-DiagnosticStage "SecureWebSocket" {
    $socket = [Net.WebSockets.ClientWebSocket]::new()
    $cancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
    try {
        try {
            $socket.ConnectAsync($webSocketEndpoint, $cancellation.Token).GetAwaiter().GetResult()
            "Connected"
        }
        catch {
            if ($_.Exception.Message -match '(?<!\d)([1-5]\d{2})(?!\d)') {
                "ServerReached HTTP " + $Matches[1]
            }
            else {
                throw
            }
        }
    }
    finally {
        $cancellation.Dispose()
        $socket.Dispose()
    }
}

$extension = Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE ".vscode\extensions") -Directory -Filter "openai.chatgpt-*" -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    Select-Object -First 1
$backend = if ($null -eq $extension) { $null } else { Join-Path $extension.FullName "bin\windows-x86_64\codex.exe" }
$temporaryHome = Join-Path ([IO.Path]::GetTempPath()) ("CodexVsCodeSwitcher-Network-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryHome | Out-Null
try {
    Invoke-DiagnosticStage "BackendStart" {
        if ([string]::IsNullOrWhiteSpace($backend) -or -not (Test-Path -LiteralPath $backend -PathType Leaf)) {
            throw "Codex backend executable was not found."
        }

        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $backend
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.ArgumentList.Add("--version")
        $startInfo.Environment["CODEX_HOME"] = $temporaryHome
        $process = [Diagnostics.Process]::Start($startInfo)
        if ($null -eq $process) { throw "Codex backend executable did not start." }
        try {
            $process.WaitForExit($TimeoutSeconds * 1000)
            if (-not $process.HasExited) { throw "Codex backend start timed out." }
            if ($process.ExitCode -ne 0) { throw "Codex backend returned a non-zero exit code." }
            "Started"
        }
        finally {
            $process.Dispose()
        }
    }
}
finally {
    if ((Test-Path -LiteralPath $temporaryHome) -and -not (Get-ChildItem -LiteralPath $temporaryHome -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $temporaryHome -Force
    }
}

$report = [pscustomobject]@{
    HttpsEndpoint = $httpsEndpoint.GetLeftPart([UriPartial]::Path)
    WebSocketEndpoint = $webSocketEndpoint.GetLeftPart([UriPartial]::Path)
    ExtensionPath = if ($null -eq $extension) { $null } else { $extension.FullName.Replace($env:USERPROFILE, "%USERPROFILE%") }
    BackendPath = if ([string]::IsNullOrWhiteSpace($backend)) { $null } else { $backend.Replace($env:USERPROFILE, "%USERPROFILE%") }
    PresentNetworkEnvironmentNames = @($networkEnvironmentNames | Where-Object { $null -ne [Environment]::GetEnvironmentVariable($_) })
    Stages = $results
}
$report | ConvertTo-Json -Depth 5

$https = $results | Where-Object Stage -eq "HTTPS" | Select-Object -First 1
if ($null -eq $https -or $https.Status -ne "Passed") {
    exit 1
}
