param([Parameter(Mandatory = $true)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $root 'OptiScalerManager\publish'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $output 'manager-stage'
$zip = Join-Path $output 'OptiScalerManager-win-x64.zip'
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zip)) {
    throw 'Manager release staging already exists. Inspect it before preparing another build.'
}
$files = @(
    'OptiScalerManager.exe', 'OptiScalerManager.dll', 'OptiScalerManager.Core.dll',
    'OptiScalerManager.deps.json', 'OptiScalerManager.runtimeconfig.json',
    'Wpf.Ui.dll', 'Wpf.Ui.Abstractions.dll',
    'dependency-inventory.json', 'source-submodules.json', 'pipeline-dependencies.json',
    'upstreams.json', 'upstreams.lock.json'
)
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($name in $files) {
    $source = Join-Path $publish $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing manager file: $name" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name)
}
Copy-Item -LiteralPath (Join-Path $publish 'Licenses') -Destination (Join-Path $stage 'Licenses') -Recurse
$version = (Get-Item -LiteralPath (Join-Path $stage 'OptiScalerManager.exe')).VersionInfo.FileVersion
if ($version.Replace(',', '.').Replace(' ', '') -ne '0.2.4.0') { throw "Unexpected manager version: $version" }
$inventory = Get-Content -LiteralPath (Join-Path $stage 'dependency-inventory.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($inventory.packageVersion -ne '0.2.4' -or $inventory.dllCount -ne 28) { throw 'Dependency inventory is not v0.2.4.' }
$pipeline = Get-Content -LiteralPath (Join-Path $stage 'pipeline-dependencies.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($pipeline.Count -ne 9) { throw "Unexpected pipeline matrix count: $($pipeline.Count)" }

Push-Location $stage
try { Compress-Archive -Path * -DestinationPath $zip -CompressionLevel Optimal }
finally { Pop-Location }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[ordered]@{
    releaseChannel = 'own'
    packageType = 'manager'
    managerVersion = '0.2.4'
    packageSha256 = $hash
} | ConvertTo-Json | Set-Content -LiteralPath "$zip.manifest.json" -Encoding UTF8
Write-Output "Prepared $zip ($hash)"
