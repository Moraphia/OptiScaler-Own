param(
    [Parameter(Mandatory = $true)][string]$BasePackage,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$base = (Resolve-Path -LiteralPath $BasePackage).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $output 'package-stage'
$zip = Join-Path $output 'OptiScaler-Package-win-x64.zip'
$expectedBase = 'C1117E937B0D2A593D80F71106F3A33A2D83491E91FF6E94F3514784CF408D36'
$testedCore = '47E62EA02D8B35246CA4325290009886CA224746CBAFF45E2E486AD536F622B0'

if ((Get-FileHash -LiteralPath $base -Algorithm SHA256).Hash -ne $expectedBase) {
    throw 'The base package is not the verified v0.2.3 release.'
}
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zip)) {
    throw 'Release staging already exists. Inspect it before preparing another build.'
}
$core = Join-Path $root 'x64\Release\a\OptiScaler.dll'
if ((Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash -ne $testedCore) {
    throw 'The OptiScaler DLL differs from the Elden Ring tested build.'
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
Expand-Archive -LiteralPath $base -DestinationPath $stage
Copy-Item -LiteralPath $core -Destination (Join-Path $stage 'OptiScaler.dll') -Force
Copy-Item -LiteralPath (Join-Path $root 'OptiScaler.ini') -Destination (Join-Path $stage 'OptiScaler.ini') -Force
@{ releaseChannel = 'own'; packageVersion = '0.2.4'; coreSha256 = $testedCore } |
    ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $stage 'package-version.json') -Encoding UTF8

$dlls = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Filter '*.dll')
if ($dlls.Count -ne 28) { throw "Unexpected DLL count: $($dlls.Count)" }
if (@(Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -match '^(ERSS|RTXMFG|RemoveFrameTimeConstraint)' }).Count -ne 0) {
    throw 'An ERSS/RTXMFG file would enter the package. Release blocked.'
}

Push-Location $stage
try { Compress-Archive -Path * -DestinationPath $zip -CompressionLevel Optimal }
finally { Pop-Location }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[ordered]@{
    releaseChannel = 'own'
    packageType = 'game'
    packageVersion = '0.2.4'
    packageSha256 = $hash
    runtimeVersions = [ordered]@{ DLSS = '310.9.1.0'; Streamline = '2.14.1.0'; StreamlineNR = '2.13.0.0'; DLSSNR = '310.8.0.0-community' }
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$zip.manifest.json" -Encoding UTF8
Write-Output "Prepared $zip ($hash)"
