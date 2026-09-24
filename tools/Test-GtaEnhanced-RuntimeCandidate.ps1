param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$CandidateDirectory,
    [switch]$Apply,
    [string]$RestoreManifest
)

$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameDirectory).Path.TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $game 'GTA5_Enhanced.exe') -PathType Leaf)) {
    throw 'The target is not a GTA V Enhanced installation.'
}
if (Get-Process GTA5_Enhanced,GTA5_Enhanced_BE,PlayGTAV -ErrorAction SilentlyContinue) {
    throw 'Close GTA V Enhanced and its launcher before changing runtime files.'
}
$be = Get-Service BEService -ErrorAction SilentlyContinue
if ($be -and $be.Status -eq 'Running') { throw 'BattlEye is running; no runtime files were changed.' }

$backupRoot = Join-Path $game 'OptiScaler\RuntimeCandidates\backup'
$allowed = @(
    'nvngx_dlss.dll', 'nvngx_dlssd.dll', 'nvngx_dlssg.dll',
    'sl.common.dll', 'sl.deepdvc.dll', 'sl.directsr.dll', 'sl.dlss.dll',
    'sl.dlss_d.dll', 'sl.dlss_g.dll', 'sl.interposer.dll', 'sl.nis.dll',
    'sl.nvperf.dll', 'sl.pcl.dll', 'sl.reflex.dll'
)

if ($RestoreManifest) {
    $manifestPath = (Resolve-Path -LiteralPath $RestoreManifest).Path
    $resolvedBackupRoot = [IO.Path]::GetFullPath($backupRoot).TrimEnd('\') + '\'
    if (-not $manifestPath.StartsWith($resolvedBackupRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Restore manifest must be inside the GTA candidate backup directory.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.gameDirectory -ne $game) { throw 'Restore manifest belongs to a different game directory.' }
    foreach ($entry in $manifest.files) {
        $name = [IO.Path]::GetFileName($entry.relative)
        if ($name -notin $allowed -or $entry.relative -notmatch '^(OptiScaler\\(streamline\\)?)?[^\\/]+\.dll$') {
            throw "Unexpected restore target: $($entry.relative)"
        }
        $backup = Join-Path (Split-Path -Parent $manifestPath) $entry.relative
        if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ne $entry.originalSha256) {
            throw "Backup verification failed: $($entry.relative)"
        }
        $installed = Join-Path $game $entry.relative
        if ((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne $entry.candidateSha256) {
            throw "Installed file has changed since candidate deployment: $($entry.relative)"
        }
    }
    foreach ($entry in $manifest.files) {
        $backup = Join-Path (Split-Path -Parent $manifestPath) $entry.relative
        $destination = Join-Path $game $entry.relative
        Copy-Item -LiteralPath $backup -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.originalSha256) {
            throw "Restore verification failed: $($entry.relative)"
        }
    }
    Write-Output "Restored $($manifest.files.Count) DLLs from $manifestPath"
    return
}

if (-not $CandidateDirectory) { throw 'CandidateDirectory is required for preview or apply.' }
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$reportPath = "$candidate.runtime-candidate.json"
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.status -ne 'local-candidate-not-released' -or $report.updatedFiles.Count -ne $allowed.Count) {
    throw 'Candidate report is missing or incomplete.'
}
$sources = @{}
foreach ($entry in $report.updatedFiles) {
    if ($entry.name -notin $allowed -or $sources.ContainsKey($entry.name)) { throw "Unexpected candidate entry: $($entry.name)" }
    if ($entry.name.StartsWith('sl.')) { $relative = "OptiScaler\streamline\$($entry.name)" }
    else { $relative = "OptiScaler\$($entry.name)" }
    $source = Join-Path $candidate $relative
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Candidate hash mismatch: $($entry.name)"
    }
    $sources[$entry.name] = $source
}

$plan = @(
    foreach ($name in $allowed) {
        $source = $sources[$name]
        if ($name.StartsWith('sl.')) { $private = "OptiScaler\streamline\$name" }
        else { $private = "OptiScaler\$name" }
        if (-not (Test-Path -LiteralPath (Join-Path $game $private))) { throw "Installed private runtime is missing: $private" }
        $private
        if (Test-Path -LiteralPath (Join-Path $game $name)) { $name }
    }
)
$entries = @(
    foreach ($relative in $plan) {
        $name = [IO.Path]::GetFileName($relative)
        $destination = Join-Path $game $relative
        [pscustomobject]@{
            relative = $relative
            originalSha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            candidateSha256 = (Get-FileHash -LiteralPath $sources[$name] -Algorithm SHA256).Hash
        }
    }
)
$entries | Format-Table relative,@{Name='Changes';Expression={$_.originalSha256 -ne $_.candidateSha256}} -AutoSize
if (-not $Apply) { Write-Output "Preview only. $($entries.Count) installed DLL paths; no changes made."; return }

$backup = Join-Path $backupRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($entry in $entries) {
    $source = Join-Path $game $entry.relative
    $destination = Join-Path $backup $entry.relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.originalSha256) {
        throw "Backup verification failed: $($entry.relative); no game DLL was changed."
    }
}
$manifestPath = Join-Path $backup 'manifest.json'
@{ gameDirectory = $game; candidateDirectory = $candidate; createdAt = (Get-Date).ToString('o'); files = $entries } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$changed = @()
try {
    foreach ($entry in $entries) {
        $destination = Join-Path $game $entry.relative
        $source = $sources[[IO.Path]::GetFileName($entry.relative)]
        $changed += $entry
        Copy-Item -LiteralPath $source -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.candidateSha256) {
            throw "Install verification failed: $($entry.relative)"
        }
    }
}
catch {
    $failure = $_
    foreach ($entry in $changed) {
        Copy-Item -LiteralPath (Join-Path $backup $entry.relative) -Destination (Join-Path $game $entry.relative) -Force
    }
    throw "Candidate deployment failed and rollback was attempted: $failure"
}
Write-Output "Updated $($entries.Count) DLL paths. Restore manifest: $manifestPath"
