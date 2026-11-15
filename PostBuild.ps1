param(
    [string]$ProjectDir,
    [string]$Configuration
)

# Every build refreshes build\<modId>, ready to copy into the game's Mods folder.
# Release builds additionally zip that folder for distribution.

Set-Location $ProjectDir

$infoJson = Get-Content "info.json" | ConvertFrom-Json
$modId = $infoJson.Id
$modVersion = $infoJson.Version

Write-Host "Assembling $modId v$modVersion ($Configuration)..."

$buildPath = "build\$modId"
$resourcesPath = "resources"
$unityProjectAssetBundlesPath = "SolarGadgets.UnityProject\Assets\AssetBundles"
$binPath = "SolarGadgets.Game\bin\$Configuration"
$dlls = @("SolarGadgets.Game.dll", "SolarGadgets.Unity.dll")

# custom_item_mod loads each item's bundle from that item's folder.
$assetBundles = @(
    @{Name="solarpanel"; Target="SolarPanel"},
    @{Name="solarbattery"; Target="SolarBattery"},
    @{Name="chargemeter"; Target="ChargeMeter"},
    @{Name="boosterconverter"; Target="BoostConverter"}
)

foreach ($bundle in $assetBundles) {
    $sourcePath = Join-Path $unityProjectAssetBundlesPath $bundle.Name
    $targetPath = Join-Path $resourcesPath $bundle.Target

    if (Test-Path $sourcePath) {
        if (!(Test-Path $targetPath)) {
            New-Item -Path $targetPath -ItemType Directory -Force | Out-Null
        }
        Copy-Item -Path $sourcePath -Destination (Join-Path $targetPath $bundle.Name) -Force
        Write-Host "  - Copied bundle $($bundle.Name) to $resourcesPath\$($bundle.Target)"
    } else {
        Write-Host "  - WARNING: AssetBundle $($bundle.Name) not found"
    }
}

if (Test-Path $buildPath) {
    Remove-Item -Path $buildPath -Recurse -Force
}
New-Item -Path $buildPath -ItemType Directory -Force | Out-Null

foreach ($dll in $dlls) {
    $dllPath = Join-Path $binPath $dll
    if (Test-Path $dllPath) {
        Copy-Item -Path $dllPath -Destination $buildPath
        Write-Host "  - Copied $dll"
    } else {
        Write-Host "  - WARNING: $dll not found at $dllPath"
    }
}

Copy-Item -Path "info.json" -Destination $buildPath
Write-Host "  - Copied info.json"

foreach ($folder in Get-ChildItem -Path $resourcesPath -Directory) {
    $itemInfo = Get-Content (Join-Path $folder.FullName "item_info.json") | ConvertFrom-Json
    if (!(Test-Path (Join-Path $folder.FullName $itemInfo.AssetBundleName))) {
        Write-Host "  - WARNING: Skipped $($folder.Name), bundle $($itemInfo.AssetBundleName) missing"
        continue
    }
    Copy-Item -Path $folder.FullName -Destination (Join-Path $buildPath $folder.Name) -Recurse -Force
    Write-Host "  - Copied $($folder.Name)"
}

Write-Host "Mod folder ready: $buildPath"

if ($Configuration -ne "Release") {
    exit 0
}

$archiveName = "${modId}_${modVersion}.zip"
$archivePath = Join-Path $ProjectDir $archiveName

Write-Host "Creating archive: $archiveName"

$7zPath = $null
$possiblePaths = @(
    "C:\Program Files\7-Zip\7z.exe",
    "C:\Program Files (x86)\7-Zip\7z.exe",
    "7z.exe"
)

foreach ($path in $possiblePaths) {
    if (Get-Command $path -ErrorAction SilentlyContinue) {
        $7zPath = $path
        break
    }
}

if ($null -eq $7zPath) {
    Write-Host "ERROR: 7z.exe not found. Please install 7-Zip or add it to PATH"
    exit 1
}

if (Test-Path $archivePath) {
    Remove-Item -Path $archivePath -Force
}

# Zip from inside the mod folder so files sit at the archive root.
Push-Location $buildPath
& $7zPath a -tzip $archivePath "*" -r
Pop-Location

if ($LASTEXITCODE -eq 0) {
    Write-Host "Archive created successfully: $archiveName"
} else {
    Write-Host "ERROR: Failed to create archive"
    exit 1
}
