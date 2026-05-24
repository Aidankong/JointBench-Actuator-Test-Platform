param(
    [string]$Python = "python",
    [switch]$SkipTests,
    [switch]$WithAds
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Invoke-PythonStep {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )
    & $Python @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $Python $($Arguments -join ' ')"
    }
}

if (-not $SkipTests) {
    Invoke-PythonStep @("-m", "pytest")
    Invoke-PythonStep @("-m", "jointbench", "--smoke-test")
    Invoke-PythonStep @("-m", "jointbench", "--protocol-dialog-smoke-test")
}

if ($WithAds) {
    Invoke-PythonStep @("-m", "pip", "install", "--no-build-isolation", "-e", ".[ads]")
} else {
    Invoke-PythonStep @("-m", "pip", "install", "--no-build-isolation", "-e", ".")
}
Invoke-PythonStep @("-m", "pip", "install", "pyinstaller")
Invoke-PythonStep @("-m", "PyInstaller", "--noconfirm", "packaging/JointBench.spec")

$distRoot = Join-Path $repo "dist\JointBench"
$distConfigs = Join-Path $distRoot "configs"
$distDocs = Join-Path $distRoot "docs"
$distTwincat = Join-Path $distRoot "twincat"
$reports = Join-Path $distRoot "reports"
if (Test-Path $distConfigs) { Remove-Item -Recurse -Force -LiteralPath $distConfigs }
if (Test-Path $distDocs) { Remove-Item -Recurse -Force -LiteralPath $distDocs }
if (Test-Path $distTwincat) { Remove-Item -Recurse -Force -LiteralPath $distTwincat }
Copy-Item -Recurse -Force -Path (Join-Path $repo "configs") -Destination $distConfigs
Copy-Item -Recurse -Force -Path (Join-Path $repo "docs") -Destination $distDocs
if (Test-Path (Join-Path $repo "twincat")) {
    Copy-Item -Recurse -Force -Path (Join-Path $repo "twincat") -Destination $distTwincat
}
New-Item -ItemType Directory -Force -Path $reports | Out-Null

Write-Host "Build complete: $(Join-Path $repo 'dist\JointBench\JointBench.exe')"
