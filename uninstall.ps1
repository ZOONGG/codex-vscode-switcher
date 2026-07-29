param(
    [switch]$RemoveNonSecretSettings
)

$ErrorActionPreference = "Stop"
$appId = "{7D04B20B-DA3C-4D7E-A85F-9B04D4180D6C}"
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexVsCodeSwitcher"
$applicationDataRoot = Join-Path $env:LOCALAPPDATA "CodexVsCodeSwitcher"
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Codex VS Code Switcher.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "Codex VS Code Switcher.lnk"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId"

Get-Process CodexVsCodeSwitcher -ErrorAction SilentlyContinue | Stop-Process -Force
foreach ($shortcut in @($startMenuShortcut, $desktopShortcut)) {
    if (Test-Path -LiteralPath $shortcut) {
        Remove-Item -LiteralPath $shortcut -Force
    }
}
if (Test-Path -LiteralPath $runKey) {
    Remove-ItemProperty -Path $runKey -Name "CodexVsCodeSwitcher" -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $uninstallKey) {
    Remove-Item -LiteralPath $uninstallKey -Recurse -Force
}

if (Test-Path -LiteralPath $installRoot) {
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}
if ($RemoveNonSecretSettings -and (Test-Path -LiteralPath $applicationDataRoot)) {
    Remove-Item -LiteralPath $applicationDataRoot -Recurse -Force
}

Write-Host "Uninstalled Codex VS Code Switcher. The main .codex directory, the original app data, legacy profiles, and dedicated profile homes were not removed."
