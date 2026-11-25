[CmdletBinding()]
param()
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$targets = @(
    (Join-Path $root "src\BlockCraft.Core\bin"), (Join-Path $root "src\BlockCraft.Core\obj"),
    (Join-Path $root "src\BlockCraft.Client\bin"), (Join-Path $root "src\BlockCraft.Client\obj"),
    (Join-Path $root "tests\BlockCraft.Tests\bin"), (Join-Path $root "tests\BlockCraft.Tests\obj"),
    (Join-Path $root "artifacts"),
    (Join-Path $root "assets\generated")
)
foreach ($target in $targets) { if (Test-Path $target) { Remove-Item $target -Recurse -Force } }
