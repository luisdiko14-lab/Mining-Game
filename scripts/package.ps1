[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^v?\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = "1.0.0",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$Version = $Version.TrimStart('v')
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\BlockCraft.Client\BlockCraft.Client.csproj"
$publishDir = Join-Path $root "artifacts\publish\$RuntimeIdentifier"
$zipPath = Join-Path $root "artifacts\BlockCraft-$Version-$RuntimeIdentifier-portable.zip"

Push-Location $root
try {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        & python -m pip install --quiet Pillow
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & python .\tools\generate_icon.py
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & python .\tools\gen_textures.py
    }
    else {
        $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
        if (-not $pyLauncher) { throw "Python 3 is required to generate original game assets." }
        & py -3 -m pip install --quiet Pillow
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & py -3 .\tools\generate_icon.py
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & py -3 .\tools\gen_textures.py
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    dotnet publish $project --configuration Release --runtime $RuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:IncludeNativeLibrariesInSingleFile=false -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false --output $publishDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if (-not (Test-Path (Join-Path $publishDir "BlockCraft.exe")) -and $RuntimeIdentifier -eq "win-x64") {
        throw "Publish output does not contain BlockCraft.exe."
    }
    # WindowsDX uses managed assemblies that are bundled into BlockCraft.exe by PublishSingleFile.
    # Direct3D itself is provided by Windows; SharpDX.Direct3D11.dll is therefore not expected
    # as a loose file beside the executable. Validate the actual published app and game assets.
    if ($RuntimeIdentifier -eq "win-x64") {
        $requiredAssets = @(
            (Join-Path $publishDir "assets\generated\block_atlas.png"),
            (Join-Path $publishDir "assets\generated\ambient_loop.wav")
        )
        foreach ($asset in $requiredAssets) {
            if (-not (Test-Path $asset)) { throw "Publish output is missing required game asset: $asset" }
        }
    }
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Portable package created: $zipPath"
}
finally { Pop-Location }
