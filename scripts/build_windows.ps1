param(
    [string]$Python = "python",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

if (-not $SkipTests) {
    & $Python -m pytest
    & $Python -m jointbench --smoke-test
    & $Python -m jointbench --protocol-dialog-smoke-test
}

& $Python -m pip install -e .
& $Python -m pip install pyinstaller
& $Python -m PyInstaller --noconfirm packaging/JointBench.spec

$distRoot = Join-Path $repo "dist\JointBench"
$distConfigs = Join-Path $distRoot "configs"
$distDocs = Join-Path $distRoot "docs"
$reports = Join-Path $distRoot "reports"
if (Test-Path $distConfigs) { Remove-Item -Recurse -Force -LiteralPath $distConfigs }
if (Test-Path $distDocs) { Remove-Item -Recurse -Force -LiteralPath $distDocs }
Copy-Item -Recurse -Force -Path (Join-Path $repo "configs") -Destination $distConfigs
Copy-Item -Recurse -Force -Path (Join-Path $repo "docs") -Destination $distDocs
New-Item -ItemType Directory -Force -Path $reports | Out-Null

Write-Host "Build complete: $(Join-Path $repo 'dist\JointBench\JointBench.exe')"
