# Builds dist/CustomProceduralParts-<version>.zip for a GitHub release (what CKAN installs):
#   GameData/CustomProceduralParts/{Spine.cfg, CustomProceduralParts.version, LICENSE.txt, README.md, Plugins/CustomProceduralParts.dll}
# Build the Release DLL first. PluginData (the user's section library and options) is never packaged.
# Checks that the csproj <Version>, the DLL's assembly version and the .version file agree.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$mod = Join-Path $root 'GameData\CustomProceduralParts'
$dll = Join-Path $mod 'Plugins\CustomProceduralParts.dll'

$csproj = [xml](Get-Content (Join-Path $root 'Source\CustomProceduralParts.csproj'))
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$avc = Get-Content (Join-Path $mod 'CustomProceduralParts.version') -Raw | ConvertFrom-Json
$avcVersion = '{0}.{1}.{2}' -f $avc.VERSION.MAJOR, $avc.VERSION.MINOR, $avc.VERSION.PATCH
$dllVersion = [Reflection.AssemblyName]::GetAssemblyName($dll).Version
$dllVersion = '{0}.{1}.{2}' -f $dllVersion.Major, $dllVersion.Minor, $dllVersion.Build
if ($avcVersion -ne $version) { throw "CustomProceduralParts.version says $avcVersion, csproj says $version" }
if ($dllVersion -ne $version) { throw "the DLL is $dllVersion, csproj says $version (rebuild)" }

$files = [ordered]@{
    'GameData/CustomProceduralParts/Spine.cfg'                            = Join-Path $mod 'Spine.cfg'
    'GameData/CustomProceduralParts/CustomProceduralParts.version'        = Join-Path $mod 'CustomProceduralParts.version'
    'GameData/CustomProceduralParts/LICENSE.txt'                          = Join-Path $root 'LICENSE.txt'
    'GameData/CustomProceduralParts/README.md'                            = Join-Path $root 'README.md'
    'GameData/CustomProceduralParts/Plugins/CustomProceduralParts.dll'    = $dll
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "CustomProceduralParts-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }

# Entry names written by hand: Compress-Archive on Windows PowerShell 5.1 stores backslashes, which
# CKAN and non-Windows unzips read as part of the file name.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($entry in $files.Keys) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $files[$entry], $entry, 'Optimal') | Out-Null
    }
}
finally { $archive.Dispose() }
"$zip ($version)"
