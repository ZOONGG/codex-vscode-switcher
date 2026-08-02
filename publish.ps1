param(
    [string]$Configuration = "Release",
    [string]$Output = "",
    [string]$ArtifactLabel = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$localDotnet = Join-Path $repo ".dotnet\dotnet.exe"
if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = [pscustomobject]@{ Source = $localDotnet }
} else {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
}

if (-not $dotnet) {
    throw "dotnet was not found. Install .NET 8 SDK or run the local SDK bootstrap used by this repository."
}

& (Join-Path $repo "build-companion.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repo "artifacts\publish"
}
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repo "artifacts"))
$Output = [System.IO.Path]::GetFullPath($Output)
if (-not $Output.StartsWith($artifactRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output must stay inside $artifactRoot."
}

if (Test-Path -LiteralPath $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

& $dotnet.Source publish (Join-Path $repo "src\CodexVsCodeSwitcher\CodexVsCodeSwitcher.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Platform=x64 `
    -o $Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$zip = Join-Path $repo "artifacts\CodexVsCodeSwitcher-win-x64-portable.zip"
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $Output "*") -DestinationPath $zip -Force

$labeledZip = $null
if (-not [string]::IsNullOrWhiteSpace($ArtifactLabel)) {
    if ($ArtifactLabel -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$') {
        throw "ArtifactLabel contains unsupported characters."
    }

    $labeledZip = Join-Path $repo "artifacts\CodexVsCodeSwitcher-$ArtifactLabel-win-x64-portable.zip"
    Copy-Item -LiteralPath $zip -Destination $labeledZip -Force
}

$checksums = @(
    "$(Get-FileHash -LiteralPath (Join-Path $Output 'CodexVsCodeSwitcher.exe') -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  artifacts/publish/CodexVsCodeSwitcher.exe",
    "$(Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  artifacts/CodexVsCodeSwitcher-win-x64-portable.zip"
)
if ($null -ne $labeledZip) {
    $checksums += "$(Get-FileHash -LiteralPath $labeledZip -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  artifacts/$(Split-Path -Leaf $labeledZip)"
}
[System.IO.File]::WriteAllLines((Join-Path $repo "artifacts\SHA256SUMS.txt"), $checksums, [System.Text.UTF8Encoding]::new($false))

Write-Host "Published to $Output"
Write-Host "Portable zip: $zip"
if ($null -ne $labeledZip) {
    Write-Host "Labeled test zip: $labeledZip"
}
