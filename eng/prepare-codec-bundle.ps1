[CmdletBinding()]
param([Parameter(Mandatory)][string] $SourceDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not [IO.Path]::IsPathFullyQualified($SourceDirectory)) { throw 'Use an absolute source directory.' }
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$destination = Join-Path $projectRoot 'vendor\imagemagick-x64'
if (Test-Path -LiteralPath $destination) { throw 'Pinned bundle already exists. Do not overwrite it; review an upgrade separately.' }
function Assert-RegularPath([string] $Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse path rejected: $cursor" }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
Assert-RegularPath $source
foreach ($required in @('magick.exe', 'License.txt', 'NOTICE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) { throw "Missing $required" }
}
$binary = [IO.File]::ReadAllBytes((Join-Path $source 'magick.exe'))
$pe = [BitConverter]::ToInt32($binary, 0x3c)
if ([BitConverter]::ToUInt16($binary, $pe + 4) -ne 0x8664) { throw 'ImageMagick must be x64.' }
$files = @(Get-ChildItem -LiteralPath $source -File | Where-Object {
    $_.Name -eq 'magick.exe' -or $_.Extension -in @('.dll', '.xml', '.icc') -or $_.Name -in @('License.txt', 'NOTICE.txt')
})
$modules = Join-Path $source 'modules'
if (Test-Path -LiteralPath $modules) {
    Assert-RegularPath $modules
    foreach ($entry in Get-ChildItem -LiteralPath $modules -Recurse -Force) { Assert-RegularPath $entry.FullName }
    $files += @(Get-ChildItem -LiteralPath $modules -Recurse -File)
}
foreach ($file in $files) { Assert-RegularPath $file.FullName }
New-Item -ItemType Directory -Path $destination | Out-Null
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'codec-policy.xml') -Destination (Join-Path $destination 'policy.xml') -Force
$inventory = @(Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($destination, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[ordered]@{
    schema = 1; architecture = 'x64'; provenance = 'Explicit ImageMagick 7 Windows distribution selected by developer';
    upstream = 'https://imagemagick.org/download/'; changed = 'policy.xml replaced by PerceptoX restricted policy';
    redistributionStatus = 'PENDING: complete delegate source/build/relink and GPL/Microsoft/graphic-rights review before public distribution';
    files = $inventory
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'bundle-manifest.json') -Encoding utf8
Write-Output "Pinned app-local codec bundle: $destination ($($inventory.Count) files). Redistribution gate remains open."
