param(
    [Parameter(Mandatory = $true)][string]$BasePackage,
    [Parameter(Mandatory = $true)][string]$DlssSdkDirectory,
    [Parameter(Mandatory = $true)][string]$StreamlineSdkDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$expectedBase = '90D2D89D7A68742CC2096EBE6F1929B6E9113E1F6EBD2352E2E5285407F09445'
$expectedDlss = '3975567B8943C53ACCE397F2B72380092F84F162D00B0D2C7D08A1025C563983'
$base = (Resolve-Path -LiteralPath $BasePackage).Path
$dlss = (Resolve-Path -LiteralPath $DlssSdkDirectory).Path
$streamline = (Resolve-Path -LiteralPath $StreamlineSdkDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$dlssDll = Join-Path $dlss 'DLSS_Sample_App\bin\ngx_dlss_demo\nvngx_dlss.dll'
$slBin = Join-Path $streamline 'bin\x64'

if ((Get-FileHash -LiteralPath $base -Algorithm SHA256).Hash -ne $expectedBase) {
    throw 'Base package is not the published v0.2.2 game ZIP.'
}
if ((Get-FileHash -LiteralPath $dlssDll -Algorithm SHA256).Hash -ne $expectedDlss) {
    throw 'DLSS SDK 310.9.1 DLL checksum mismatch.'
}
if ((Get-FileHash -LiteralPath (Join-Path $slBin 'nvngx_dlss.dll') -Algorithm SHA256).Hash -ne $expectedDlss) {
    throw 'DLSS DLL in Streamline SDK differs from the official DLSS SDK copy.'
}
if (Test-Path -LiteralPath $output) { throw "Candidate output already exists: $output" }

$sources = @(
    'nvngx_dlss.dll', 'nvngx_dlssd.dll', 'nvngx_dlssg.dll',
    'sl.common.dll', 'sl.deepdvc.dll', 'sl.directsr.dll', 'sl.dlss.dll',
    'sl.dlss_d.dll', 'sl.dlss_g.dll', 'sl.interposer.dll', 'sl.nis.dll',
    'sl.nvperf.dll', 'sl.pcl.dll', 'sl.reflex.dll'
)
foreach ($name in $sources) {
    $source = Join-Path $slBin $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing official runtime: $name" }
    $signature = Get-AuthenticodeSignature -LiteralPath $source
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'NVIDIA') {
        throw "Runtime does not have a valid NVIDIA signature: $name"
    }
    $expectedVersion = if ($name.StartsWith('nvngx_')) { '310,9,1,0' } else { '2,14,1,0' }
    if ((Get-Item -LiteralPath $source).VersionInfo.FileVersion -ne $expectedVersion) {
        throw "Unexpected official runtime version: $name"
    }
}

Expand-Archive -LiteralPath $base -DestinationPath $output
$runtimeDirectory = Join-Path $output 'OptiScaler'
$streamlineDirectory = Join-Path $runtimeDirectory 'streamline'
foreach ($name in $sources) {
    $destination = if ($name.StartsWith('sl.')) { Join-Path $streamlineDirectory $name } else { Join-Path $runtimeDirectory $name }
    Copy-Item -LiteralPath (Join-Path $slBin $name) -Destination $destination -Force
}

# The public 2.14.1 SDK does not ship sl.dlss_nr.dll. Keep the existing 2.13
# plugin for isolated compatibility testing; do not claim a fully unified set.
$nrPlugin = Join-Path $streamlineDirectory 'sl.dlss_nr.dll'
if (-not (Test-Path -LiteralPath $nrPlugin)) { throw 'The v0.2.2 NR Streamline plugin is missing.' }
$nrModel = Join-Path $runtimeDirectory 'nvngx_dlssnr.dll'
if ((Get-FileHash -LiteralPath $nrModel -Algorithm SHA256).Hash -ne 'E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A') {
    throw 'NR model changed unexpectedly.'
}

$rows = foreach ($name in $sources) {
    $path = if ($name.StartsWith('sl.')) { Join-Path $streamlineDirectory $name } else { Join-Path $runtimeDirectory $name }
    [ordered]@{
        name = $name
        version = (Get-Item -LiteralPath $path).VersionInfo.FileVersion
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        signature = (Get-AuthenticodeSignature -LiteralPath $path).Status.ToString()
    }
}
$report = [ordered]@{
    status = 'local-candidate-not-released'
    baseRelease = 'v0.2.2'
    dlssSdk = 'v310.9.1'
    streamlineSdk = 'v2.14.1'
    retainedNrPlugin = 'sl.dlss_nr.dll 2.13.0.0'
    retainedNrModelSha256 = (Get-FileHash -LiteralPath $nrModel -Algorithm SHA256).Hash
    updatedFiles = @($rows)
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$output.runtime-candidate.json" -Encoding UTF8
Write-Output "Staged candidate at $output"
