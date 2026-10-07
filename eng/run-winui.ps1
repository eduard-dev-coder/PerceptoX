[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $MagickPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable = Join-Path $projectRoot "src\PerceptoX.WinUI\bin\x64\$Configuration\net10.0-windows10.0.19041.0\PerceptoX.WinUI.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Construiți întâi aplicația $Configuration x64." }
if ($MagickPath -and $Configuration -ne 'Debug') {
    throw 'Release folosește decoderul inclus în pachet. Pentru un runtime extern de dezvoltare folosiți -Configuration Debug; pentru standalone rulați executabilul publicat.'
}
$previousCodec = [Environment]::GetEnvironmentVariable('PERCEPTOX_MAGICK_PATH', 'Process')
try {
    if ($MagickPath) {
        if (-not [IO.Path]::IsPathFullyQualified($MagickPath) -or -not (Test-Path -LiteralPath $MagickPath -PathType Leaf)) {
            throw 'Specificați calea absolută către magick.exe instalat local.'
        }
        $env:PERCEPTOX_MAGICK_PATH = [IO.Path]::GetFullPath($MagickPath)
    }
    Start-Process -FilePath $executable -WorkingDirectory $projectRoot -WindowStyle Hidden
} finally {
    [Environment]::SetEnvironmentVariable('PERCEPTOX_MAGICK_PATH', $previousCodec, 'Process')
}
