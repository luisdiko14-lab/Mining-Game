[CmdletBinding()]
param([switch]$NoRestore)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw ".NET 10 SDK is required." }
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
    if (-not $NoRestore) {
        dotnet restore .\BlockCraft.sln
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    dotnet build .\BlockCraft.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet test .\BlockCraft.sln --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally { Pop-Location }
