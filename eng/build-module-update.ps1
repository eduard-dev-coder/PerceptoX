[CmdletBinding()]
param(
    [Parameter(Mandatory)][version] $Version,
    [Parameter(Mandatory)][uri] $PackageUrl,
    [Parameter(Mandatory)][string] $PrivateKeyFile,
    [Parameter(Mandatory)][string] $LicenseSummary
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PackageUrl.Scheme -ne 'https' -or $PackageUrl.IsLoopback -or $PackageUrl.UserInfo) { throw 'Use an approved HTTPS package URL.' }
if ([string]::IsNullOrWhiteSpace($LicenseSummary) -or $LicenseSummary.Length -gt 4096) { throw 'Supply reviewed license information.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$payload = Join-Path $projectRoot 'vendor\imagemagick-x64'
$manifest = Get-Content -LiteralPath (Join-Path $payload 'bundle-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $file = [IO.Path]::GetFullPath((Join-Path $payload $entry.path))
    if (-not $file.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'Codec payload hash mismatch.' }
}
$delivery = Join-Path $projectRoot ('artifacts\module-updates\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $delivery | Out-Null
$archive = Join-Path $delivery 'module.zip'
# Zip contains only pinned files plus the inventory, not arbitrary contents of vendor.
$stage = Join-Path $delivery 'payload'
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($entry in $manifest.files) {
    $target = Join-Path $stage $entry.path
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $payload $entry.path) -Destination $target
}
Copy-Item -LiteralPath (Join-Path $payload 'bundle-manifest.json') -Destination $stage
$stageFiles = @(Get-ChildItem -LiteralPath $stage -Force)
Compress-Archive -LiteralPath $stageFiles.FullName -DestinationPath $archive -CompressionLevel Optimal
$offer = [ordered]@{ schema=1; architecture='win-x64'; protocol='perceptox-module-v1'; module='imagemagick';
    version=$Version.ToString(); packageUrl=$PackageUrl.AbsoluteUri; sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash;
    bytes=(Get-Item -LiteralPath $archive).Length; expiresUtc=[DateTimeOffset]::UtcNow.AddDays(7).ToString('O'); licenseSummary=$LicenseSummary }
$bytes = [Text.Encoding]::UTF8.GetBytes(($offer | ConvertTo-Json -Compress))
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKeyFile).Path))
    if ($rsa.KeySize -lt 3072) { throw 'Signing key must be RSA 3072-bit or stronger.' }
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    @{payload=[Convert]::ToBase64String($bytes); signature=[Convert]::ToBase64String($signature)} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $delivery 'catalog.json') -Encoding utf8
    $rsa.ExportSubjectPublicKeyInfoPem() | Set-Content -LiteralPath (Join-Path $delivery 'public-key.pem') -Encoding ascii
} finally { $rsa.Dispose() }
Write-Output "Signed candidate: $delivery. No upload performed. Complete license/source review before publishing. Never distribute the private key."
