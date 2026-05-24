param(
    [string]$ExePath = ".\dist\JointBench\JointBench.exe"
)

$ErrorActionPreference = "Stop"
$resolved = Resolve-Path $ExePath

$main = Start-Process -FilePath $resolved -ArgumentList "--smoke-test" -Wait -PassThru
if ($main.ExitCode -ne 0) {
    throw "Packaged main window smoke test failed."
}

$dialog = Start-Process -FilePath $resolved -ArgumentList "--protocol-dialog-smoke-test" -Wait -PassThru
if ($dialog.ExitCode -ne 0) {
    throw "Packaged protocol dialog smoke test failed."
}

Write-Host "Packaged smoke tests passed: $resolved"
