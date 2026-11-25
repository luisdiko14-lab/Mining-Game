[CmdletBinding()]
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $exe = Join-Path $root "src\BlockCraft.Client\bin\$Configuration\net10.0\BlockCraft.exe"
    if (-not (Test-Path $exe)) {
        & .\scripts\build.ps1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    if (-not (Test-Path $exe)) { throw "Game executable was not produced: $exe" }
    & $exe
    exit $LASTEXITCODE
}
finally { Pop-Location }
