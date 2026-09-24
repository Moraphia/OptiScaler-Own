param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$PackageZip,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$zip = (Resolve-Path -LiteralPath $PackageZip).Path
$expected = 'C1117E937B0D2A593D80F71106F3A33A2D83491E91FF6E94F3514784CF408D36'
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) {
    throw 'The input is not the verified v0.2.3 release package.'
}

function Get-Source([string]$relative) {
    $name = [IO.Path]::GetFileName($relative)
    if ($name -eq 'OptiScaler.dll' -or $name -eq 'nvngx.dll_dlssnr.dll') { return 'This repository (built from source)' }
    if ($name -eq 'nvngx_dlssnr.dll') { return 'Aurora community binary; modified signature' }
    if ($name -eq 'sl.dlss_nr.dll') { return 'Retained from v0.2.2; NVIDIA Streamline 2.13' }
    if ($name -like 'sl.*.dll') { return 'NVIDIA Streamline 2.14.1' }
    if ($name -like 'nvngx_dlss*.dll') { return 'NVIDIA DLSS / Streamline 310.9.1' }
    if ($name -like 'amd_fidelityfx_*.dll') { return 'AMD FidelityFX runtime' }
    if ($name -like 'libxes*.dll' -or $name -eq 'libxell.dll') { return 'Intel XeSS / XeLL runtime' }
    if ($name -eq 'dlssg_to_fsr3_amd_is_better.dll') { return 'Nukem dlssg-to-fsr3 compatibility binary' }
    if ($name -eq 'D3D12Core.dll') { return 'Microsoft D3D12Core' }
    return 'Provenance to verify'
}

$rows = @(Get-ChildItem -LiteralPath $package -Recurse -File |
    Where-Object { $_.Extension -ieq '.dll' } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($package.Length + 1).Replace('\', '/')
        $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
        [ordered]@{
            path = $relative
            version = if ([string]::IsNullOrWhiteSpace($_.VersionInfo.FileVersion)) { 'not specified' } else { $_.VersionInfo.FileVersion.Replace(',', '.').Replace(' ', '') }
            source = Get-Source $relative
            signature = $signature.Status.ToString()
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
if ($rows.Count -ne 28) { throw "Expected 28 DLLs in v0.2.3 release; found $($rows.Count)." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $dllEntries = @($archive.Entries | Where-Object { $_.FullName -like '*.dll' })
    if ($dllEntries.Count -ne $rows.Count) { throw 'ZIP and extracted DLL counts differ.' }
    foreach ($row in $rows) {
        $entry = $dllEntries | Where-Object { $_.FullName.Replace('\', '/') -eq $row.path } | Select-Object -First 1
        if ($null -eq $entry) { throw "Missing ZIP entry: $($row.path)" }
        $stream = $entry.Open()
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $archiveHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
        finally { $hasher.Dispose(); $stream.Dispose() }
        if ($archiveHash -ne $row.sha256) { throw "Extracted DLL differs from release ZIP: $($row.path)" }
    }
} finally { $archive.Dispose() }
$inventory = [ordered]@{ packageVersion = '0.2.3'; packageSha256 = $expected; dllCount = $rows.Count; files = $rows }
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output "Generated $($rows.Count) verified release DLL entries."
