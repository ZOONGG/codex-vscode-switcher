param(
    [string]$VsCodeExecutable = "D:\Programss\code\Microsoft VS Code\Code.exe",
    [int]$WindowTimeoutSeconds = 60
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
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}
"@
Add-Type -TypeDefinition $source

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("CodexVsCodeSwitcher-Smoke-" + [Guid]::NewGuid().ToString("N"))
$codexHome = Join-Path $temporaryRoot "codex-home"
$userData = Join-Path $temporaryRoot "user-data"
$extensions = Join-Path $temporaryRoot "extensions"
$sharedData = Join-Path $temporaryRoot "shared-data"
New-Item -ItemType Directory -Path $codexHome, $userData, $extensions, $sharedData | Out-Null

function Get-ExactManagedProcesses {
    Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Where-Object {
        $processPath = $_.ExecutablePath
        $commandLine = $_.CommandLine
        -not [string]::IsNullOrWhiteSpace($processPath) -and
        -not [string]::IsNullOrWhiteSpace($commandLine) -and
        [IO.Path]::GetFullPath($processPath).Equals($executable, [StringComparison]::OrdinalIgnoreCase) -and
        $commandLine.Contains($userData, [StringComparison]::OrdinalIgnoreCase) -and
        $commandLine.Contains($extensions, [StringComparison]::OrdinalIgnoreCase) -and
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

$ordinaryBefore = @(Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Select-Object -ExpandProperty ProcessId)
$results = @()
try {
    foreach ($iteration in 1..2) {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $executable
        $startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($executable)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $false
        $startInfo.ArgumentList.Add("--user-data-dir")
        $startInfo.ArgumentList.Add($userData)
        $startInfo.ArgumentList.Add("--extensions-dir")
        $startInfo.ArgumentList.Add($extensions)
        $startInfo.ArgumentList.Add("--shared-data-dir")
        $startInfo.ArgumentList.Add($sharedData)
        $startInfo.ArgumentList.Add("--disable-extensions")
        $startInfo.ArgumentList.Add("--disable-workspace-trust")
        $startInfo.ArgumentList.Add("--new-window")
        $startInfo.Environment["CODEX_HOME"] = $codexHome
        $root = [Diagnostics.Process]::Start($startInfo)
        if ($null -eq $root) {
            throw "VS Code could not be started."
        }

        $window = Wait-ForExactWindow -RootProcessId $root.Id
        [IntPtr]$handle = [IntPtr]$window.WindowHandle
        $focusRequested = [CodexVsCodeSmokeNative]::SetForegroundWindow($handle)
        Start-Sleep -Milliseconds 250
        $focused = [CodexVsCodeSmokeNative]::GetForegroundWindow() -eq $handle
        Close-ExactManagedWindow -WindowHandle $window.WindowHandle
        $results += [pscustomobject]@{
            Iteration = $iteration
            RootProcessId = $window.RootProcessId
            WindowProcessId = $window.WindowProcessId
            LauncherHandoff = $window.RootProcessId -ne $window.WindowProcessId
            WindowHandle = $window.WindowHandle
            ExactArgumentOwnership = $true
            FocusRequested = $focusRequested
            FocusVerified = $focused
            GracefulCloseVerified = $true
        }
    }

    $ordinaryAfter = @(Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Select-Object -ExpandProperty ProcessId)
    [pscustomobject]@{
        ExecutablePath = $executable
        TemporaryRoot = $temporaryRoot
        OrdinaryProcessIdsBefore = $ordinaryBefore
        OrdinaryProcessIdsAfter = $ordinaryAfter
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
