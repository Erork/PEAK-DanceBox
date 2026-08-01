param([string]$GamePath = "")
$ErrorActionPreference = "Stop"
& "$PSScriptRoot\build.ps1" -GamePath $GamePath -Install -Package
if (-not $?) { exit 1 }
