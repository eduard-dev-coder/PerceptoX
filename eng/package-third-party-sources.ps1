[CmdletBinding()]
param([Parameter(Mandatory)][string] $DeliveryDirectory, [switch] $VerifyOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delivery = [IO.Path]::GetFullPath($DeliveryDirectory)
$allowed = Join-Path $projectRoot 'artifacts\distribution'
if (-not $delivery.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Third-party sources must be packaged within distribution artifacts.' }
$source = Join-Path $projectRoot 'vendor\sources\imagemagick-7.1.2-26'
$archiveName = 'ImageMagick-7.1.2-26-Windows.7z'
$archive = Join-Path $source $archiveName
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'Run eng/fetch-codec-sources.ps1 before publishing; matching-version native sources are mandatory for the candidate.' }
if ((Get-Item -LiteralPath (Join-Path $projectRoot 'vendor\imagemagick-x64\magick.exe')).VersionInfo.ProductVersion -ne '7.1.2-26') { throw 'Pinned third-party sources do not match the codec version; review and update source provenance first.' }
if ((Get-Item -LiteralPath $archive).Length -ne 796180195L -or
    (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'CC94B39F81CEDB0AB4991E510EBCAE13F891E038210DDC6402D15562C9448C17') { throw 'Third-party source archive changed.' }
foreach ($path in @($source, $delivery)) {
    $cursor = $path
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse source packaging path rejected.' }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
if ((Get-Item -LiteralPath $archive -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse source archive rejected.' }
if ($VerifyOnly) { Write-Output 'Pinned native source archive and codec version verified.'; return }
$target = Join-Path $delivery 'ThirdParty-sources'
if (Test-Path -LiteralPath $target) { throw 'Do not overwrite an existing third-party source snapshot.' }
New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -LiteralPath $archive -Destination $target
Copy-Item -LiteralPath (Join-Path $source 'source-provenance.json') -Destination $target
Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor\imagemagick-x64\NOTICE.txt') -Destination $target
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\THIRD_PARTY_SOURCES.md') -Destination $target
if ((Get-FileHash -LiteralPath (Join-Path $target $archiveName) -Algorithm SHA256).Hash -ne 'CC94B39F81CEDB0AB4991E510EBCAE13F891E038210DDC6402D15562C9448C17') { throw 'Third-party source copy failed verification.' }
Write-Output "Third-party source materials: $target; not a legal clearance."
