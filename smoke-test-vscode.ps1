param(
    [string]$VsCodeExecutable = "D:\Programss\code\Microsoft VS Code\Code.exe",
    [int]$WindowTimeoutSeconds = 60,
    [ValidateSet("All", "Shared", "Isolated")]
    [string]$ExtensionMode = "All"
)

$ErrorActionPreference = "Stop"
$executable = [IO.Path]::GetFullPath($VsCodeExecutable)
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "VS Code executable was not found: $executable"
}

$source = @"
using System;
using System.Runtime.InteropServices;
public static class CodexVsCodeSmokeNative {
    private const int SW_RESTORE = 9;

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public static bool FocusWindow(IntPtr hWnd) {
        IntPtr foreground = GetForegroundWindow();
        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint targetThread = GetWindowThreadProcessId(hWnd, out _);
        bool attachedForeground = foregroundThread != 0 && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);
        bool attachedTarget = targetThread != 0 && targetThread != currentThread
            && targetThread != foregroundThread && AttachThreadInput(currentThread, targetThread, true);

        try {
            ShowWindowAsync(hWnd, SW_RESTORE);
            BringWindowToTop(hWnd);
            return SetForegroundWindow(hWnd);
        }
        finally {
            if (attachedTarget) {
                AttachThreadInput(currentThread, targetThread, false);
            }
            if (attachedForeground) {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}
"@
Add-Type -TypeDefinition $source

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("CodexVsCodeSwitcher-Smoke-" + [Guid]::NewGuid().ToString("N"))
$codexHome = Join-Path $temporaryRoot "codex-home"
$userData = Join-Path $temporaryRoot "user-data"
$extensions = Join-Path $temporaryRoot "extensions"
$sharedData = Join-Path $temporaryRoot "shared-data"
New-Item -ItemType Directory -Path $codexHome, $userData, $extensions, $sharedData | Out-Null
$currentMode = "Isolated"
$networkEnvironmentNames = @(
    "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
    "CODEX_CA_CERTIFICATE", "SSL_CERT_FILE", "SSL_CERT_DIR",
    "NODE_EXTRA_CA_CERTS", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE"
)
$presentNetworkEnvironmentNames = @($networkEnvironmentNames | Where-Object {
    $null -ne [Environment]::GetEnvironmentVariable($_)
})

function Get-ExactManagedProcesses {
    Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Where-Object {
        $processPath = $_.ExecutablePath
        $commandLine = $_.CommandLine
        -not [string]::IsNullOrWhiteSpace($processPath) -and
        -not [string]::IsNullOrWhiteSpace($commandLine) -and
        [IO.Path]::GetFullPath($processPath).Equals($executable, [StringComparison]::OrdinalIgnoreCase) -and
        $commandLine.Contains($userData, [StringComparison]::OrdinalIgnoreCase) -and
        (($currentMode -eq "Isolated" -and
            $commandLine.Contains("--extensions-dir", [StringComparison]::OrdinalIgnoreCase) -and
            $commandLine.Contains($extensions, [StringComparison]::OrdinalIgnoreCase)) -or
         ($currentMode -eq "Shared" -and
            -not $commandLine.Contains("--extensions-dir", [StringComparison]::OrdinalIgnoreCase))) -and
        $commandLine.Contains($sharedData, [StringComparison]::OrdinalIgnoreCase)
    }
}

function Wait-ForExactWindow {
    param([int]$RootProcessId)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($WindowTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $matches = @(Get-ExactManagedProcesses)
        foreach ($match in $matches) {
            $process = Get-Process -Id $match.ProcessId -ErrorAction SilentlyContinue
            if ($null -ne $process -and $process.MainWindowHandle -ne [IntPtr]::Zero) {
                return [pscustomobject]@{
                    RootProcessId = $RootProcessId
                    WindowProcessId = [int]$match.ProcessId
                    WindowHandle = [long]$process.MainWindowHandle
                    MatchingProcessIds = @($matches.ProcessId)
                }
            }
        }

        Start-Sleep -Milliseconds 250
    }

    throw "An exact matching VS Code window did not appear within $WindowTimeoutSeconds seconds."
}

function Close-ExactManagedWindow {
    param([long]$WindowHandle)

    $matches = @(Get-ExactManagedProcesses)
    $windowOwner = $matches | Where-Object {
        $process = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
        $null -ne $process -and [long]$process.MainWindowHandle -eq $WindowHandle
    } | Select-Object -First 1
    if ($null -eq $windowOwner) {
        throw "The verified smoke-test window owner is no longer available."
    }

    $process = Get-Process -Id $windowOwner.ProcessId
    if (-not $process.CloseMainWindow()) {
        throw "VS Code rejected the graceful close request."
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (@(Get-ExactManagedProcesses).Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "The exact matching VS Code processes did not exit after the graceful close request."
}

function Set-ExactManagedWindowForeground {
    param([long]$WindowHandle)

    [IntPtr]$handle = [IntPtr]$WindowHandle
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    $focusRequested = $false
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $focusRequested = [CodexVsCodeSmokeNative]::FocusWindow($handle) -or $focusRequested
        Start-Sleep -Milliseconds 100
        if ([CodexVsCodeSmokeNative]::GetForegroundWindow() -eq $handle) {
            return [pscustomobject]@{
                Requested = $focusRequested
                Verified = $true
            }
        }
    }

    throw "The exact matching VS Code window could not be verified as foreground."
}

$ordinaryBefore = @(Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Select-Object -ExpandProperty ProcessId)
$results = @()
try {
    $modes = if ($ExtensionMode -eq "All") { @("Shared", "Isolated") } else { @($ExtensionMode) }
    if ($modes -contains "Shared") {
        $codeCommand = Join-Path ([IO.Path]::GetDirectoryName($executable)) "bin\code.cmd"
        if (-not (Test-Path -LiteralPath $codeCommand -PathType Leaf)) {
            throw "VS Code CLI was not found for the shared-extension verification."
        }

        $sharedExtensions = @(& $codeCommand --list-extensions --show-versions --user-data-dir $userData)
        if ($LASTEXITCODE -ne 0 -or -not ($sharedExtensions -match '^openai\.chatgpt@')) {
            throw "Shared mode did not discover the normal openai.chatgpt extension installation."
        }
    }

    foreach ($mode in $modes) {
      $currentMode = $mode
      foreach ($iteration in 1..2) {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $executable
        $startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($executable)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $false
        $startInfo.ArgumentList.Add("--user-data-dir")
        $startInfo.ArgumentList.Add($userData)
        if ($currentMode -eq "Isolated") {
            $startInfo.ArgumentList.Add("--extensions-dir")
            $startInfo.ArgumentList.Add($extensions)
        }
        $startInfo.ArgumentList.Add("--shared-data-dir")
        $startInfo.ArgumentList.Add($sharedData)
        if ($currentMode -eq "Isolated") {
            $startInfo.ArgumentList.Add("--disable-extensions")
        }
        $startInfo.ArgumentList.Add("--disable-workspace-trust")
        $startInfo.ArgumentList.Add("--new-window")
        $startInfo.Environment["CODEX_HOME"] = $codexHome
        $root = [Diagnostics.Process]::Start($startInfo)
        if ($null -eq $root) {
            throw "VS Code could not be started."
        }

        $window = Wait-ForExactWindow -RootProcessId $root.Id
        $focus = Set-ExactManagedWindowForeground -WindowHandle $window.WindowHandle
        Close-ExactManagedWindow -WindowHandle $window.WindowHandle
        $results += [pscustomobject]@{
            ExtensionMode = $currentMode
            Iteration = $iteration
            RootProcessId = $window.RootProcessId
            WindowProcessId = $window.WindowProcessId
            LauncherHandoff = $window.RootProcessId -ne $window.WindowProcessId
            WindowHandle = $window.WindowHandle
            ExactArgumentOwnership = $true
            ExtensionsArgumentPresent = $currentMode -eq "Isolated"
            ParentNetworkEnvironmentNamesPreserved = $presentNetworkEnvironmentNames
            FocusRequested = $focus.Requested
            FocusVerified = $focus.Verified
            GracefulCloseVerified = $true
        }
      }
    }

    $ordinaryAfter = @(Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Select-Object -ExpandProperty ProcessId)
    $ordinaryDifference = @(Compare-Object -ReferenceObject $ordinaryBefore -DifferenceObject $ordinaryAfter)
    if ($ordinaryDifference.Count -ne 0) {
        throw "The ordinary VS Code process set changed during the isolated smoke test."
    }

    [pscustomobject]@{
        ExecutablePath = $executable
        TemporaryRoot = $temporaryRoot
        OrdinaryProcessIdsBefore = $ordinaryBefore
        OrdinaryProcessIdsAfter = $ordinaryAfter
        OrdinaryProcessesUntouched = $true
        Iterations = $results
    } | ConvertTo-Json -Depth 5
}
finally {
    if (@(Get-ExactManagedProcesses).Count -eq 0 -and (Test-Path -LiteralPath $temporaryRoot)) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedTempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolvedTemporary.StartsWith($resolvedTempBase, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
        }
    }
}
