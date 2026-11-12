<#
.SYNOPSIS
    Builds the mod and packages it as dist\AutoCackle-<version>.zip.

.PARAMETER Install
    Also copies the built mod into the game's Mods folder (the game must be closed).

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Install
#>
[CmdletBinding()]
param(
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$modId = 'AutoCackle'

# Info.json is the single source of the version shown by Unity Mod Manager.
$info = Get-Content (Join-Path $root 'Info.json') -Raw | ConvertFrom-Json
$version = $info.Version

dotnet build (Join-Path $root "$modId.csproj") -c Release --nologo -v q "-p:Version=$version"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$built = Join-Path $root "bin\$modId"
$files = @("$modId.dll", 'Info.json')
foreach ($file in $files) {
    if (-not (Test-Path (Join-Path $built $file))) { throw "Missing build output: $file" }
}

# The zip holds one folder named after the mod id, which is what the Mods folder expects.
# Entries are written one by one with forward slashes: Compress-Archive in Windows PowerShell
# writes backslashes, which do not extract as folders on Linux (Steam Deck) or macOS.
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "$modId-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, (Join-Path $built $file), "$modId/$file") | Out-Null
    }
}
finally {
    $archive.Dispose()
}
Write-Host "Package: $zip"

if ($Install) {
    # Same lookup as the project file: GamePath.props if present, otherwise the default Steam path.
    $gamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure'
    $props = Join-Path $root 'GamePath.props'
    if (Test-Path $props) {
        $configured = ([xml](Get-Content $props -Raw)).Project.PropertyGroup.WrathPath
        if ($configured) { $gamePath = $configured }
    }

    $mods = Join-Path $gamePath 'Mods'
    if (-not (Test-Path $mods)) { throw "Mods folder not found: $mods" }

    $target = Join-Path $mods $modId
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($file in $files) { Copy-Item (Join-Path $built $file) $target -Force }
    Write-Host "Installed: $target"
}
