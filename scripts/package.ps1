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
        & python -m pip install --quiet -r .\tools\requirements.txt
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & python .\tools\generate_icon.py
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & python .\tools\gen_textures.py
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & python .\tools\gen_soundtrack.py
    }
    else {
        $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
        if (-not $pyLauncher) { throw "Python 3 is required to generate original game assets." }
        & py -3 -m pip install --quiet -r .\tools\requirements.txt
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & py -3 .\tools\generate_icon.py
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & py -3 .\tools\gen_textures.py
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & py -3 .\tools\gen_soundtrack.py
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $musicDir = Join-Path $root "assets\generated\music"
    $musicFiles = @(Get-ChildItem $musicDir -Filter "*.wav" -File -ErrorAction Stop)
    $musicBytes = ($musicFiles | Measure-Object -Property Length -Sum).Sum
    if ($musicFiles.Count -lt 18 -or $musicBytes -lt 180000000) {
        throw "Original soundtrack pack is incomplete: found $($musicFiles.Count) tracks and $musicBytes bytes; expected 18 tracks and at least 180 MB."
    }
    Write-Host "Original soundtrack verified: $($musicFiles.Count) tracks, $([math]::Round($musicBytes / 1MB, 1)) MiB."

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
            (Join-Path $publishDir "assets\generated\ambient_loop.wav"),
            (Join-Path $publishDir "assets\generated\music\music_manifest.json")
        )
        foreach ($asset in $requiredAssets) {
            if (-not (Test-Path $asset)) { throw "Publish output is missing required game asset: $asset" }
        }
    }
    $checkerProject = Join-Path $root "src\Mining_Game.Error_Checker\Mining_Game.Error_Checker.csproj"
    $checkerPublishDir = Join-Path $root "artifacts\publish\checker-$RuntimeIdentifier"
    if (Test-Path $checkerPublishDir) { Remove-Item $checkerPublishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $checkerPublishDir -Force | Out-Null
    dotnet publish $checkerProject --configuration Release --runtime $RuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false --output $checkerPublishDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $checkerExe = Join-Path $checkerPublishDir "Mining_Game_Error_Checker.exe"
    if (-not (Test-Path $checkerExe)) { throw "The GUI error checker executable was not produced." }
    Copy-Item $checkerExe (Join-Path $publishDir "Mining_Game_Error_Checker.exe") -Force
    Copy-Item $checkerExe (Join-Path $publishDir "Mining_Game.exe") -Force
    Write-Host "Added Mining_Game.exe launcher and Mining_Game_Error_Checker.exe."

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel NoCompression
    Write-Host "Portable package created: $zipPath"
}
finally { Pop-Location }
