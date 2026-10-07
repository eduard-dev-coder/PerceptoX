[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory = Join-Path $projectRoot 'vendor\sources\imagemagick-7.1.2-26'
$name = 'ImageMagick-7.1.2-26-Windows.7z'
$url = "https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-26/$name"
$sha256 = 'CC94B39F81CEDB0AB4991E510EBCAE13F891E038210DDC6402D15562C9448C17'
$length = 796180195L
# Do not follow directory junctions to write outside the workspace.
$cursor = $directory
while ($cursor) {
    if (Test-Path -LiteralPath $cursor) {
        if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse source directory rejected.' }
    }
    $cursor = [IO.Path]::GetDirectoryName($cursor)
}
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$archive = Join-Path $directory $name
function Confirm-Archive([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse source archive rejected.' }
    if ($item.Length -ne $length -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $sha256) { throw 'Official source archive size or SHA-256 mismatch.' }
}
if (Test-Path -LiteralPath $archive) {
    Confirm-Archive $archive
} else {
    $temporary = Join-Path $directory ($name + '.' + [Guid]::NewGuid().ToString('N') + '.partial')
    Write-Output "Downloading pinned upstream Windows sources ($length bytes); no software is installed."
    Invoke-WebRequest -Uri $url -OutFile $temporary -TimeoutSec 600
    Confirm-Archive $temporary
    Move-Item -LiteralPath $temporary -Destination $archive
}
[ordered]@{
    schema = 1
    component = 'ImageMagick Windows source distribution'
    version = '7.1.2-26'
    sourceRelease = 'https://github.com/ImageMagick/ImageMagick/releases/tag/7.1.2-26'
    sourceUrl = $url
    archive = $name
    length = $length
    sha256 = $sha256
    binaryRelease = 'ImageMagick-7.1.2-26-Q16-HDRI-x64-dll.exe'
    binaryReleaseSha256 = 'E629C8A7DC1C18F79226D5C0218E64B8102A1CFC96B9A16D891BAC04D263D093'
    status = 'Official matching-version source archive collected; component/source/build and relink review required.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory 'source-provenance.json') -Encoding utf8
Write-Output "Verified source archive: $archive"
