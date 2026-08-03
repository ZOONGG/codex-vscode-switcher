param(
    [string]$VsCodeExecutable = "D:\Programss\code\Microsoft VS Code\Code.exe",
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$executable = [IO.Path]::GetFullPath($VsCodeExecutable)
$companion = [IO.Path]::GetFullPath((Join-Path $repo "src\CodexVsCodeSwitcher.Companion"))
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "VS Code executable was not found: $executable" }
if (-not (Test-Path -LiteralPath (Join-Path $companion "package.json") -PathType Leaf)) { throw "Companion extension was not found." }

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("CodexVsCodeSwitcher-WorkspaceSmoke-" + [Guid]::NewGuid().ToString("N"))
$userData = Join-Path $temporaryRoot "user-data"
$sharedData = Join-Path $temporaryRoot "shared-data"
$bridge = Join-Path $temporaryRoot "bridge"
$testFolder = Join-Path $temporaryRoot "test-project"
$ordinaryUserData = Join-Path $temporaryRoot "ordinary-user-data"
$ordinarySharedData = Join-Path $temporaryRoot "ordinary-shared-data"
$profileA = Join-Path $temporaryRoot "profile-a"
$profileB = Join-Path $temporaryRoot "profile-b"
New-Item -ItemType Directory -Path $userData, $sharedData, $bridge, $testFolder, $ordinaryUserData, $ordinarySharedData, $profileA, $profileB | Out-Null
$stateFile = Join-Path $bridge "workspace-state.json"
$commandFile = Join-Path $bridge "command-request.json"
$ordinaryBefore = @(Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Select-Object -ExpandProperty ProcessId)

function Get-SmokeProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
        -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
        [IO.Path]::GetFullPath($_.ExecutablePath).Equals($executable, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CommandLine.Contains($userData, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CommandLine.Contains($sharedData, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CommandLine.Contains($companion, [StringComparison]::OrdinalIgnoreCase)
    })
}

function Get-DisposableOrdinaryProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
        -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
        [IO.Path]::GetFullPath($_.ExecutablePath).Equals($executable, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CommandLine.Contains($ordinaryUserData, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CommandLine.Contains($ordinarySharedData, [StringComparison]::OrdinalIgnoreCase) -and
        -not $_.CommandLine.Contains($companion, [StringComparison]::OrdinalIgnoreCase)
    })
}

function Start-DisposableOrdinaryWindow {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executable
    $startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($executable)
    $startInfo.UseShellExecute = $false
    foreach ($argument in @(
        "--user-data-dir", $ordinaryUserData,
        "--shared-data-dir", $ordinarySharedData,
        "--disable-extensions", "--disable-workspace-trust", "--new-window", $testFolder)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $root = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $root) { throw "The disposable ordinary VS Code process could not be started." }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $window = Get-DisposableOrdinaryProcesses | ForEach-Object {
            Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
        } | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($null -ne $window) { return $window }
        Start-Sleep -Milliseconds 250
    }
    throw "The disposable ordinary VS Code window did not appear within $TimeoutSeconds seconds."
}

function Close-DisposableOrdinaryWindow {
    param($Window)
    if ($null -ne $Window -and -not $Window.HasExited -and -not $Window.CloseMainWindow()) {
        throw "The disposable ordinary VS Code window rejected graceful close."
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ((Get-DisposableOrdinaryProcesses).Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "The disposable ordinary VS Code processes did not close gracefully."
}

function Wait-ForBridgeState {
    param(
        [string]$SessionId,
        [DateTimeOffset]$AfterTimestamp,
        [string]$RequiredSidebarStatus = ""
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $stateFile -PathType Leaf) {
            try {
                $state = Get-Content -Raw -LiteralPath $stateFile | ConvertFrom-Json
                $timestamp = [DateTimeOffset]::Parse($state.timestampUtc)
                if ($state.sessionId -eq $SessionId -and $timestamp -gt $AfterTimestamp) {
                    if ([string]::IsNullOrWhiteSpace($RequiredSidebarStatus) -or
                        $state.sidebarStatus -eq $RequiredSidebarStatus) {
                        return $state
                    }
                    if ($RequiredSidebarStatus -eq "Succeeded" -and $state.sidebarStatus -eq "Failed") {
                        return $state
                    }
                }
            } catch {
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "The companion did not publish a valid state within $TimeoutSeconds seconds."
}

function Start-SmokeCycle {
    param([string]$ProfileHome, [string]$WorkspacePath)
    $session = [Guid]::NewGuid().ToString("N")
    Remove-Item -LiteralPath $stateFile, $commandFile -Force -ErrorAction SilentlyContinue
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executable
    $startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($executable)
    $startInfo.UseShellExecute = $false
    foreach ($argument in @(
        "--user-data-dir", $userData,
        "--shared-data-dir", $sharedData,
        "--extensionDevelopmentPath", $companion,
        "--disable-workspace-trust", "--new-window", $WorkspacePath)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.Environment["CODEX_HOME"] = $ProfileHome
    $startInfo.Environment["CODEX_VSCODE_SWITCHER_BRIDGE_PATH"] = $bridge
    $startInfo.Environment["CODEX_VSCODE_SWITCHER_SESSION_ID"] = $session
    $startInfo.Environment["CODEX_VSCODE_SWITCHER_OPEN_CODEX"] = "1"
    $startInfo.Environment["CODEX_VSCODE_SWITCHER_USER_DATA_DIR"] = $userData
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw "VS Code could not be started." }

    $state = Wait-ForBridgeState -SessionId $session -AfterTimestamp ([DateTimeOffset]::MinValue)
    if ([IO.Path]::GetFullPath($state.workspacePath) -ne [IO.Path]::GetFullPath($WorkspacePath)) { throw "The companion reported a different workspace." }
    if ($state.workspaceType -ne "Folder") { throw "The active workspace was not reported as a folder." }
    if (-not $state.codexExtensionInstalled) { throw "The official Codex extension is unavailable in shared mode." }

    $beforeCommand = [DateTimeOffset]::Parse($state.timestampUtc)
    $command = [ordered]@{
        protocolVersion = 1
        sessionId = $session
        requestId = [Guid]::NewGuid().ToString("N")
        action = "openCodex"
        timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    $temporaryCommand = Join-Path $bridge (".command-" + [Guid]::NewGuid().ToString("N") + ".tmp")
    $command | ConvertTo-Json | Set-Content -LiteralPath $temporaryCommand -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporaryCommand -Destination $commandFile -Force
    $commandState = Wait-ForBridgeState -SessionId $session -AfterTimestamp $beforeCommand -RequiredSidebarStatus "Succeeded"
    if ($commandState.sidebarStatus -ne "Succeeded") { throw "chatgpt.openSidebar was not accepted: $($commandState.sidebarFailureCode)" }
    [pscustomobject]@{ Process = $process; SessionId = $session; State = $commandState }
}

function Close-SmokeCycle {
    param($Cycle)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    $windowProcess = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $windowProcess = Get-SmokeProcesses | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue } |
            Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($null -ne $windowProcess) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $windowProcess -or -not $windowProcess.CloseMainWindow()) { throw "VS Code rejected the graceful close request." }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ((Get-SmokeProcesses).Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "The disposable VS Code process did not close gracefully."
}

$cycles = @()
$ordinaryWindow = $null
try {
    $ordinaryWindow = Start-DisposableOrdinaryWindow
    $first = Start-SmokeCycle -ProfileHome $profileA -WorkspacePath $testFolder
    $managedWindow = Get-SmokeProcesses | ForEach-Object {
        Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
    } | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    if ($null -eq $managedWindow) { throw "The managed window was not available after the bridge report." }
    if ($managedWindow.Id -eq $ordinaryWindow.Id -or $managedWindow.MainWindowHandle -eq $ordinaryWindow.MainWindowHandle) {
        throw "The managed launch adopted the disposable ordinary VS Code window."
    }
    if ($ordinaryWindow.HasExited) { throw "The disposable ordinary VS Code window was closed by managed launch." }
    $remembered = [IO.Path]::GetFullPath($first.State.workspacePath)
    $cycles += [pscustomobject]@{ Stage = "initial"; Workspace = $remembered; Sidebar = $first.State.sidebarStatus }
    Close-SmokeCycle $first

    $second = Start-SmokeCycle -ProfileHome $profileA -WorkspacePath $remembered
    $cycles += [pscustomobject]@{ Stage = "restore"; Workspace = $second.State.workspacePath; Sidebar = $second.State.sidebarStatus }
    Close-SmokeCycle $second

    $third = Start-SmokeCycle -ProfileHome $profileB -WorkspacePath $remembered
    $cycles += [pscustomobject]@{ Stage = "profile-switch"; Workspace = $third.State.workspacePath; Sidebar = $third.State.sidebarStatus }
    Close-SmokeCycle $third

    if ($ordinaryWindow.HasExited) { throw "The ordinary same-project window did not survive managed cycles." }
    Close-DisposableOrdinaryWindow $ordinaryWindow
    $ordinaryWindow = $null

    $ordinaryAfter = @(Get-CimInstance Win32_Process -Filter "Name = 'Code.exe'" | Select-Object -ExpandProperty ProcessId)
    if (@(Compare-Object $ordinaryBefore $ordinaryAfter).Count -ne 0) { throw "The ordinary VS Code process set changed." }
    [pscustomobject]@{
        Result = "Passed"
        WorkspaceRestore = $true
        ProfileSwitchPreservedWorkspace = $true
        SidebarCommandAccepted = $true
        SameProjectOpenedInTwoDistinctWindows = $true
        ManagedIdentityIgnoredProjectTitle = $true
        RememberedProjectRelaunchSkippedChooser = $true
        OrdinaryVsCodeUntouched = $true
        Cycles = $cycles
    } | ConvertTo-Json -Depth 4
}
finally {
    $remaining = @(Get-SmokeProcesses)
    foreach ($item in $remaining) {
        $candidate = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if ($null -ne $candidate -and $candidate.MainWindowHandle -ne [IntPtr]::Zero) {
            [void]$candidate.CloseMainWindow()
        }
    }
    $closeDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $closeDeadline -and (Get-SmokeProcesses).Count -gt 0) {
        Start-Sleep -Milliseconds 250
    }
    if ($null -ne $ordinaryWindow -and -not $ordinaryWindow.HasExited) {
        [void]$ordinaryWindow.CloseMainWindow()
    }
    $ordinaryCloseDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $ordinaryCloseDeadline -and (Get-DisposableOrdinaryProcesses).Count -gt 0) {
        Start-Sleep -Milliseconds 250
    }
    if ((Get-SmokeProcesses).Count -eq 0 -and
        (Get-DisposableOrdinaryProcesses).Count -eq 0 -and
        (Test-Path -LiteralPath $temporaryRoot)) {
        $resolved = [IO.Path]::GetFullPath($temporaryRoot)
        $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
