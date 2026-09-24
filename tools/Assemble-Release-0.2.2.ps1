param()

$ErrorActionPreference = 'Stop'
$repo = 'Moraphia/OptiScaler-Own'
$tag = 'v0.2.2'
$expectedModel = 'E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A'
$expectedCore = '5D122DBFC775B9DAFB01D031D9E70693DED1FD41C6420FA84228422D34513BB4'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $env:RUNNER_TEMP 'optiscaler-own-v0.2.2'
$inputs = Join-Path $work 'inputs'
$stage = Join-Path $work 'package'
$output = Join-Path $work 'OptiScaler-Package-win-x64.zip'

New-Item -ItemType Directory -Path $inputs, $stage -Force | Out-Null

gh release download v0.2.1 -R $repo -p 'OptiScaler-Package-win-x64.zip' -D $inputs
if ($LASTEXITCODE -ne 0) { throw 'Could not download v0.2.1 base package.' }
gh release download $tag -R $repo -p 'OptiScaler.dll.part*' -p 'nvngx.dll_dlssnr.dll' -D $inputs
if ($LASTEXITCODE -ne 0) { throw 'Could not download v0.2.2 build inputs.' }
gh release download aurora-runtime-dlssnr-310.8 -R abc354402600/OptiScaler-Aurora -p 'nvngx_dlssnr.dll' -D $inputs
if ($LASTEXITCODE -ne 0) { throw 'Could not download the Aurora NR runtime.' }

if ((Get-FileHash -Algorithm SHA256 (Join-Path $inputs 'nvngx_dlssnr.dll')).Hash -ne $expectedModel) {
    throw 'Aurora NR runtime checksum mismatch.'
}

Expand-Archive -LiteralPath (Join-Path $inputs 'OptiScaler-Package-win-x64.zip') -DestinationPath $stage

$corePath = Join-Path $stage 'OptiScaler.dll'
$coreOutput = [System.IO.File]::Create($corePath)
try {
    foreach ($index in 0..4) {
        $part = Join-Path $inputs ('OptiScaler.dll.part{0:D2}' -f $index)
        if (-not (Test-Path -LiteralPath $part)) { throw "Missing core part $index" }
        $input = [System.IO.File]::OpenRead($part)
        try { $input.CopyTo($coreOutput) } finally { $input.Dispose() }
    }
} finally { $coreOutput.Dispose() }
if ((Get-FileHash -Algorithm SHA256 $corePath).Hash -ne $expectedCore) {
    throw 'Reassembled core DLL checksum mismatch.'
}

Copy-Item -LiteralPath (Join-Path $root 'OptiScaler.ini') -Destination (Join-Path $stage 'OptiScaler.ini') -Force
Copy-Item -LiteralPath (Join-Path $inputs 'nvngx.dll_dlssnr.dll') -Destination (Join-Path $stage 'nvngx.dll_dlssnr.dll')
Copy-Item -LiteralPath (Join-Path $inputs 'nvngx_dlssnr.dll') -Destination (Join-Path $stage 'OptiScaler\nvngx_dlssnr.dll')
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_RUNTIME.md') -Destination (Join-Path $stage 'Licenses\DLSSNR_PROVENANCE.md')
Copy-Item -LiteralPath (Join-Path $stage 'OptiScaler\streamline\nvngx_dlss.license.txt') -Destination (Join-Path $stage 'Licenses\NVIDIA_RTX_SDK_LICENSE.txt')

Push-Location $stage
try { Compress-Archive -Path * -DestinationPath $output -CompressionLevel Optimal } finally { Pop-Location }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($output)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('OptiScaler.dll', 'OptiScaler.ini', 'nvngx.dll_dlssnr.dll',
                            'OptiScaler/nvngx_dlssnr.dll', 'Licenses/DLSSNR_PROVENANCE.md')) {
        if ($required -notin $names) { throw "Package is missing $required" }
    }
} finally { $archive.Dispose() }

$manifest = [ordered]@{
    releaseChannel = 'own'
    packageType = 'game'
    packageVersion = '0.2.2'
    packageSha256 = (Get-FileHash -Algorithm SHA256 $output).Hash
}
$manifestPath = "$output.manifest.json"
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

gh release upload $tag $output $manifestPath -R $repo
if ($LASTEXITCODE -ne 0) { throw 'Could not upload the assembled game package.' }

Write-Output "Uploaded game package: $($manifest.packageSha256)"
