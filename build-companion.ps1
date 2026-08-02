$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$extension = Join-Path $repo "src\CodexVsCodeSwitcher.Companion"
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
    throw "Node.js is required to validate the bundled companion extension."
}

& $node.Source --check (Join-Path $extension "extension.js")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $node.Source --test (Join-Path $extension "companion.test.js")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$manifest = Get-Content -Raw -LiteralPath (Join-Path $extension "package.json") | ConvertFrom-Json
if ($manifest.publisher -ne "ZOONGG" -or $manifest.name -ne "codex-vscode-switcher-companion") {
    throw "The companion extension identity is invalid."
}

Write-Host "Companion extension validated: ZOONGG.codex-vscode-switcher-companion"
