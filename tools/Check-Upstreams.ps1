param(
    [string]$Manifest = "$PSScriptRoot\..\upstreams.json",
    [string]$Lock = "$PSScriptRoot\..\upstreams.lock.json",
    [string]$Report = "$PSScriptRoot\..\upstream-report.md",
    [switch]$UpdateLock
)

$ErrorActionPreference = 'Stop'
$headers = @{ 'User-Agent' = 'OptiScaler-Upstream-Monitor'; 'Accept' = 'application/vnd.github+json' }
$manifestObject = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$old = @{}
if (Test-Path -LiteralPath $Lock) {
    $lockObject = Get-Content -LiteralPath $Lock -Raw | ConvertFrom-Json
    foreach ($entry in @($lockObject.upstreams)) { $old[$entry.id] = $entry }
}

$current = [System.Collections.Generic.List[object]]::new()
$changes = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($item in @($manifestObject.upstreams | Where-Object enabled)) {
    $api = "https://api.github.com/repos/$($item.repository)"
    try {
        if ($item.updatePolicy -eq 'release') {
            $remote = Invoke-RestMethod -Uri "$api/releases/latest" -Headers $headers
            $version = [string]$remote.tag_name
            $url = [string]$remote.html_url
        } else {
            $remote = Invoke-RestMethod -Uri "$api/commits/$($item.ref)" -Headers $headers
            $version = [string]$remote.sha
            $url = [string]$remote.html_url
        }
        $entry = [ordered]@{ id = $item.id; repository = $item.repository; ref = $item.ref; policy = $item.updatePolicy; version = $version; url = $url; checkedAt = [DateTime]::UtcNow.ToString('o') }
        $current.Add([pscustomobject]$entry)
        $previous = if ($old.ContainsKey($item.id)) { [string]$old[$item.id].version } else { '<not locked>' }
        if ($previous -ne $version) { $changes.Add([pscustomobject]@{ id = $item.id; repository = $item.repository; previous = $previous; current = $version; url = $url }) }
    } catch {
        $failures.Add("$($item.repository): $($_.Exception.Message)")
        $current.Add([pscustomobject]@{ id = $item.id; repository = $item.repository; ref = $item.ref; policy = $item.updatePolicy; version = 'unavailable'; url = $api; checkedAt = [DateTime]::UtcNow.ToString('o'); error = $_.Exception.Message })
        Write-Warning "Could not query $($item.repository): $($_.Exception.Message)"
    }
}

$reportLines = @('# Upstream report', '', "Checked: $([DateTime]::UtcNow.ToString('u'))", '')
if ($failures.Count -gt 0) { $reportLines += "Incomplete: $($failures.Count) upstream query failure(s). No-update status cannot be determined."; $reportLines += ''; $reportLines += @($failures | ForEach-Object { "- $_" }) }
if ($changes.Count -eq 0 -and $failures.Count -eq 0) { $reportLines += 'No upstream version changes detected.' }
else {
    $reportLines += '| Dependency | Previous | Current |'
    $reportLines += '|---|---|---|'
    foreach ($change in $changes) { $reportLines += "| [$($change.id)]($($change.url)) | ``$($change.previous)`` | ``$($change.current)`` |" }
    $reportLines += ''; $reportLines += '> Source synchronization remains review-gated. This report does not redistribute restricted binaries.'
}
[IO.File]::WriteAllText($Report, ($reportLines -join "`r`n") + "`r`n", [Text.UTF8Encoding]::new($false))
if ($UpdateLock -and $failures.Count -eq 0) {
    [IO.File]::WriteAllText($Lock, (@{ schemaVersion = 1; checkedAt = [DateTime]::UtcNow.ToString('o'); upstreams = @($current) } | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
Write-Host "Checked $($current.Count) upstreams; changes: $($changes.Count)"
if ($failures.Count -gt 0) { exit 2 }
if ($changes.Count -gt 0) { exit 10 }
