param(
    [string]$Source = "",
    [switch]$StartWithWindows,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $repo "artifacts\publish"
}

$sourceExe = Join-Path $Source "CodexVsCodeSwitcher.exe"
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "CodexVsCodeSwitcher.exe was not found in $Source. Run .\publish.ps1 first."
}
$Source = Split-Path -Parent (Resolve-Path -LiteralPath $sourceExe).Path

$appId = "{7D04B20B-DA3C-4D7E-A85F-9B04D4180D6C}"
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\CodexVsCodeSwitcher"
$shortcutDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuShortcut = Join-Path $shortcutDir "Codex VS Code Switcher.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "Codex VS Code Switcher.lnk"

Get-Process CodexVsCodeSwitcher -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
Copy-Item -Path (Join-Path $Source "*") -Destination $installRoot -Recurse -Force

function New-CodexVsCodeSwitcherShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Target
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $Target
    $link.WorkingDirectory = Split-Path -Parent $Target
    $link.Description = "Codex VS Code Switcher"
    $link.IconLocation = "$Target,0"
    $link.Save()
}

$installedExe = Join-Path $installRoot "CodexVsCodeSwitcher.exe"
New-CodexVsCodeSwitcherShortcut -Path $startMenuShortcut -Target $installedExe
New-CodexVsCodeSwitcherShortcut -Path $desktopShortcut -Target $installedExe

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if ($StartWithWindows) {
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name "CodexVsCodeSwitcher" -Value ('"{0}"' -f $installedExe)
}

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId"
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "Codex VS Code Switcher"
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $installedExe
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "ZOONGG"
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f (Join-Path $repo "uninstall.ps1"))
New-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -PropertyType DWord -Force | Out-Null

if ($Launch) {
    Start-Process -FilePath $installedExe
}

Write-Host "Installed to $installRoot"
