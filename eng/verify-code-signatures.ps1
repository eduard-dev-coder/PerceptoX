[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [string] $InstallerPath,
    [switch] $RequireValid
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$paths = @(
    (Join-Path $package 'PerceptoX.exe'),
    (Join-Path $package 'app\PerceptoX.WinUI.exe')
)
if ($InstallerPath) { $paths += (Resolve-Path -LiteralPath $InstallerPath).Path }
$failed = $false
foreach ($path in $paths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing signing target: $path" }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    [pscustomobject]@{
        Path = $path
        Status = [string]$signature.Status
        Signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
        TimestampSigner = if ($signature.TimeStamperCertificate) { $signature.TimeStamperCertificate.Subject } else { $null }
    }
    if ($signature.Status -ne 'Valid') { $failed = $true }
}
if ($RequireValid -and $failed) { throw 'One or more PerceptoX artifacts have no valid trusted Authenticode signature.' }
