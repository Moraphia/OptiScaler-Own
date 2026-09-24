param()

$ErrorActionPreference = 'Stop'
$repo = 'Moraphia/OptiScaler-Own'
$tag = 'v0.2.3'
$expectedCore = '6B6598F5F03386970A5C1CD6B07516F3B955CF4D43A631792F19F314783AF499'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $env:RUNNER_TEMP 'optiscaler-own-v0.2.3'
$inputs = Join-Path $work 'inputs'
$dlssSdk = Join-Path $work 'dlss-sdk'
$streamlineSdk = Join-Path $work 'streamline-sdk'
$stage = Join-Path $work 'package'
$output = Join-Path $work 'OptiScaler-Package-win-x64.zip'

New-Item -ItemType Directory -Path $inputs -Force | Out-Null
gh release download v0.2.2 -R $repo -p 'OptiScaler-Package-win-x64.zip' -D $inputs
if ($LASTEXITCODE -ne 0) { throw 'Could not download the v0.2.2 base package.' }
gh release download $tag -R $repo -p 'OptiScaler.dll.part*' -D $inputs
if ($LASTEXITCODE -ne 0) { throw 'Could not download the v0.2.3 core parts.' }
gh release download v310.9.1 -R NVIDIA/DLSS -p 'ngx_dlss_demo_windows.zip' -D $inputs
if ($LASTEXITCODE -ne 0) { throw 'Could not download the official DLSS SDK.' }
gh release download v2.14.1 -R NVIDIA-RTX/Streamline -p 'streamline-sdk-v2.14.1.zip' -D $inputs
if ($LASTEXITCODE -ne 0) { throw 'Could not download the official Streamline SDK.' }

$officialArchives = @{
    'ngx_dlss_demo_windows.zip' = '21C0511C6B45E80C9D0338F504F4F986D261D52050F7FE2AF480E3C42581FC35'
    'streamline-sdk-v2.14.1.zip' = '92C4D954631A1710DA86CA3FA8D5034F2B9503838C95FC4AE977AE149319781B'
}
foreach ($name in $officialArchives.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $inputs $name) -Algorithm SHA256).Hash -ne $officialArchives[$name]) {
        throw "Official SDK archive checksum mismatch: $name"
    }
}
Expand-Archive -LiteralPath (Join-Path $inputs 'ngx_dlss_demo_windows.zip') -DestinationPath $dlssSdk
Expand-Archive -LiteralPath (Join-Path $inputs 'streamline-sdk-v2.14.1.zip') -DestinationPath $streamlineSdk
& (Join-Path $PSScriptRoot 'Stage-Nvidia-Runtime-Candidate.ps1') `
    -BasePackage (Join-Path $inputs 'OptiScaler-Package-win-x64.zip') `
    -DlssSdkDirectory $dlssSdk -StreamlineSdkDirectory $streamlineSdk -OutputDirectory $stage
if ($LASTEXITCODE -ne 0) { throw 'Candidate staging failed.' }

$corePath = Join-Path $stage 'OptiScaler.dll'
$coreOutput = [IO.File]::Create($corePath)
try {
    foreach ($index in 0..4) {
        $part = Join-Path $inputs ('OptiScaler.dll.part{0:D2}' -f $index)
        if (-not (Test-Path -LiteralPath $part)) { throw "Missing core part $index" }
        $input = [IO.File]::OpenRead($part)
        try { $input.CopyTo($coreOutput) } finally { $input.Dispose() }
    }
} finally { $coreOutput.Dispose() }
if ((Get-FileHash -LiteralPath $corePath -Algorithm SHA256).Hash -ne $expectedCore) {
    throw 'Reassembled v0.2.3 core checksum mismatch.'
}
Copy-Item -LiteralPath (Join-Path $root 'OptiScaler.ini') -Destination (Join-Path $stage 'OptiScaler.ini') -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_RUNTIME.md') -Destination (Join-Path $stage 'Licenses\DLSSNR_PROVENANCE.md') -Force
@{ releaseChannel = 'own'; packageVersion = '0.2.3'; coreSha256 = $expectedCore } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'package-version.json') -Encoding UTF8

Push-Location $stage
try { Compress-Archive -Path * -DestinationPath $output -CompressionLevel Optimal } finally { Pop-Location }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($output)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('OptiScaler.dll', 'OptiScaler.ini', 'package-version.json', 'OptiScaler/nvngx_dlss.dll',
                            'OptiScaler/nvngx_dlssnr.dll', 'OptiScaler/streamline/sl.interposer.dll',
                            'OptiScaler/streamline/sl.dlss_nr.dll', 'Licenses/DLSSNR_PROVENANCE.md')) {
        if ($required -notin $names) { throw "Package is missing $required" }
    }
} finally { $archive.Dispose() }

$manifest = [ordered]@{
    releaseChannel = 'own'
    packageType = 'game'
    packageVersion = '0.2.3'
    packageSha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    runtimeVersions = [ordered]@{
        DLSS = '310.9.1.0'
        Streamline = '2.14.1.0'
        StreamlineNR = '2.13.0.0'
        DLSSNR = '310.8.0.0-community'
    }
}
$manifestPath = "$output.manifest.json"
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
gh release upload $tag $output $manifestPath -R $repo
if ($LASTEXITCODE -ne 0) { throw 'Could not upload the assembled game package.' }
Write-Output "Uploaded v0.2.3 game package: $($manifest.packageSha256)"
