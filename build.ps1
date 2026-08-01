param(
    [string]$GamePath = "",
    [switch]$Install,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found. Install the .NET 8 SDK, reopen PowerShell, then run this script again."
}

function Resolve-PeakPath([string]$RequestedPath) {
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($RequestedPath) { $candidates.Add($RequestedPath) }

    try {
        $steamPath = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -ErrorAction Stop).SteamPath
        if ($steamPath) { $candidates.Add((Join-Path $steamPath "steamapps\common\PEAK")) }
    } catch {}

    $candidates.Add("C:\Program Files (x86)\Steam\steamapps\common\PEAK")
    $candidates.Add("C:\Program Files\Steam\steamapps\common\PEAK")
    $candidates.Add("D:\SteamLibrary\steamapps\common\PEAK")
    $candidates.Add("E:\SteamLibrary\steamapps\common\PEAK")

    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        $full = [System.IO.Path]::GetFullPath($candidate)
        if (Test-Path (Join-Path $full "PEAK_Data\Managed\Assembly-CSharp.dll")) {
            return $full.TrimEnd('\', '/')
        }
    }

    throw "PEAK game directory was not found. Run: .\build.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\PEAK' -Install"
}

$peakRoot = Resolve-PeakPath $GamePath
$escapedRoot = $peakRoot.Replace("\", "/") + "/"
@"
<Project>
  <PropertyGroup>
    <PeakGameRootDir>$escapedRoot</PeakGameRootDir>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path "Config.Build.user.props" -Encoding UTF8

Write-Host "Building against: $peakRoot"

# Never allow a failed build to package or install an older DLL left in artifacts.
if (Test-Path "artifacts") { Remove-Item "artifacts" -Recurse -Force }

dotnet restore "DanceBox.sln"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE. Nothing was installed or packaged."
}

dotnet build "DanceBox.sln" -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE. Nothing was installed or packaged."
}

$danceDll = Get-ChildItem -Path "artifacts" -Recurse -Filter "com.dline.dancebox.dll" -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $danceDll) {
    throw "Build completed but the main plugin DLL was not found under artifacts/."
}

$outputDir = $danceDll.Directory.FullName
Write-Host "Main build output: $outputDir"

$stageRoot = Join-Path $PSScriptRoot "dist\DanceBox"
$pluginStage = Join-Path $stageRoot "plugins\DanceBox"
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $pluginStage -Force | Out-Null

Copy-Item $danceDll.FullName $pluginStage -Force

$bundleOutput = Join-Path $outputDir "bundles"
if (Test-Path $bundleOutput) {
    Copy-Item $bundleOutput (Join-Path $pluginStage "bundles") -Recurse -Force
} else {
    Copy-Item (Join-Path $PSScriptRoot "assets\original-bundles") (Join-Path $pluginStage "bundles") -Recurse -Force
}

$musicOutput = Join-Path $outputDir "music"
if (Test-Path $musicOutput) {
    Copy-Item $musicOutput (Join-Path $pluginStage "music") -Recurse -Force
} else {
    Copy-Item (Join-Path $PSScriptRoot "assets\music") (Join-Path $pluginStage "music") -Recurse -Force
}

$modelOutput = Join-Path $outputDir "model-bundles"
if (Test-Path $modelOutput) {
    Copy-Item $modelOutput (Join-Path $pluginStage "model-bundles") -Recurse -Force
} else {
    Copy-Item (Join-Path $PSScriptRoot "assets\model-bundles") (Join-Path $pluginStage "model-bundles") -Recurse -Force
}

Copy-Item (Join-Path $PSScriptRoot "README.md") $stageRoot -Force
Copy-Item (Join-Path $PSScriptRoot "CHANGELOG.md") $stageRoot -Force
Copy-Item (Join-Path $PSScriptRoot "icon.png") $stageRoot -Force
Copy-Item (Join-Path $PSScriptRoot "manifest.json") $stageRoot -Force
Copy-Item (Join-Path $PSScriptRoot "licenses") $stageRoot -Recurse -Force

$required = @(
    (Join-Path $pluginStage "com.dline.dancebox.dll"),
    (Join-Path $pluginStage "model-bundles\MODEL_INDEX.txt"),
    (Join-Path $pluginStage "model-bundles\xiehen_model_01_xiehen70.bundle")
)
foreach ($file in $required) {
    if (-not (Test-Path $file)) { throw "Required packaged file is missing: $file" }
}

if ($Install) {
    $pluginsRoot = Join-Path $peakRoot "BepInEx\plugins"
    if (-not (Test-Path $pluginsRoot)) {
        throw "BepInEx plugins directory does not exist: $pluginsRoot. Install/run BepInEx first."
    }

    $target = Join-Path $pluginsRoot "DanceBox"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item (Join-Path $pluginStage "*") $target -Recurse -Force

    # Remove legacy package locations so DanceBox is not loaded twice after an upgrade.
    foreach ($legacyName in @("PEAKLethalDancesComplete", "PEAKLethalDances")) {
        $legacyFolder = Join-Path $pluginsRoot $legacyName
        if (Test-Path $legacyFolder) { Remove-Item $legacyFolder -Recurse -Force }
    }
    Get-ChildItem -Path $pluginsRoot -Recurse -Filter "com.nadiyajafi.peaklethaldances*.dll" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Host "Installed to: $target"
}

if ($Package) {
    $zipPath = Join-Path $PSScriptRoot "dist\DanceBox_v2.0.8.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Package created: $zipPath"
}

Write-Host "Done. Start PEAK through BepInEx and inspect BepInEx\LogOutput.log for 'DanceBox'."
