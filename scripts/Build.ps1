param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$BepInExDir
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'StutterBuddies.csproj'
& dotnet build $project -c Release "-p:GameDir=$GameDir" "-p:BepInExDir=$BepInExDir" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
& dotnet run --project (Join-Path $projectRoot 'tests\StutterBuddies.Tests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
$package = Join-Path $projectRoot 'package'
$pluginDestination = Join-Path $package 'BepInEx\plugins\StutterBuddies'
New-Item -ItemType Directory -Path $pluginDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Release\netstandard2.1\StutterBuddies.dll') -Destination $pluginDestination -Force
foreach ($name in @('manifest.json', 'README.md', 'CHANGELOG.md', 'LICENSE', 'icon.png')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $package -Force
}
$zipPath = Join-Path (Split-Path -Parent $projectRoot) 'StutterBuddies-0.1.0.zip'
# Package only these known files, so a stray dependency can never be bundled.
$entries = @('manifest.json', 'README.md', 'CHANGELOG.md', 'LICENSE', 'icon.png', 'BepInEx\plugins\StutterBuddies\StutterBuddies.dll')
Add-Type -AssemblyName System.IO.Compression
$file = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
try {
    $zip = [System.IO.Compression.ZipArchive]::new($file, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($name in $entries) {
            $entry = $zip.CreateEntry($name.Replace('\','/'), [System.IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            $source = [System.IO.File]::OpenRead((Join-Path $package $name))
            try { $source.CopyTo($entryStream) } finally { $source.Dispose(); $entryStream.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $file.Dispose() }
Write-Output "Package: $zipPath"
