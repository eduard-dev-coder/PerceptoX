[CmdletBinding()]
param([switch] $OfflineRestore)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'validate-languages.ps1')
& (Join-Path $PSScriptRoot 'validate-theme.ps1')
$bundle = Join-Path $projectRoot 'vendor\imagemagick-x64'
$manifestPath = Join-Path $bundle 'bundle-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'First run eng/prepare-codec-bundle.ps1 with a reviewed ImageMagick x64 distribution.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.architecture -ne 'x64') { throw 'Unsupported codec manifest.' }
$expected = @{}
foreach ($entry in $manifest.files) {
    if ([IO.Path]::IsPathRooted($entry.path)) { throw 'Absolute codec manifest path rejected.' }
    $path = [IO.Path]::GetFullPath((Join-Path $bundle $entry.path))
    if (-not $path.StartsWith($bundle + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Codec manifest escaped bundle.' }
    if ($expected.ContainsKey($path)) { throw 'Duplicate codec manifest path.' }
    $expected[$path] = $true
    $cursor = $path
    while ($cursor) {
        if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse codec path rejected: $cursor" }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Codec changed: $($entry.path)" }
}
foreach ($file in Get-ChildItem -LiteralPath $bundle -Recurse -File) {
    if ($file.FullName -ne $manifestPath -and -not $expected.ContainsKey($file.FullName)) { throw "Unpinned codec file: $($file.Name)" }
}
$buildId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$delivery = Join-Path $projectRoot "artifacts\distribution\$buildId"
$output = Join-Path $delivery 'PerceptoX-win-x64'
$applicationOutput = Join-Path $output 'app'
$documentationOutput = Join-Path $output 'docs'
$licenseOutput = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $applicationOutput,$documentationOutput,$licenseOutput -Force | Out-Null
& (Join-Path $PSScriptRoot 'package-third-party-sources.ps1') -DeliveryDirectory $delivery -VerifyOnly
$arguments = @('publish', (Join-Path $projectRoot 'src\PerceptoX.WinUI\PerceptoX.WinUI.csproj'),
    '-p:PublishProfile=StandaloneX64', '-p:Platform=x64', '-r', 'win-x64', '--self-contained', 'true',
    '--output', $applicationOutput, '--verbosity', 'minimal')
if ($OfflineRestore) { $arguments += @('-p:NuGetAudit=false', '-p:WarningsNotAsErrors=NU1801', '--ignore-failed-sources') }
& (Join-Path $PSScriptRoot 'dotnet-sandbox.ps1') -DotNetArguments $arguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; partial output retained for diagnosis, no ZIP created.' }
$codecOutput = Join-Path $applicationOutput 'codecs\imagemagick'
New-Item -ItemType Directory -Path $codecOutput -Force | Out-Null
foreach ($entry in Get-ChildItem -LiteralPath $bundle -Force) { Copy-Item -LiteralPath $entry.FullName -Destination $codecOutput -Recurse }
foreach ($file in @('STANDALONE.md','MODULE_MANAGER.md','LOCALIZATION.md','OFFLINE_ACCEPTANCE.md','UI_SETUP_PLAN_2026-10-07.md','UI_SETUP_IMPLEMENTATION_2026-10-07.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot ('docs\' + $file)) -Destination $documentationOutput }
foreach ($file in @('LICENSE', 'NOTICE')) { Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $licenseOutput }
& (Join-Path $PSScriptRoot 'collect-distribution-notices.ps1') -PackageDirectory $applicationOutput -LicenseDirectory $licenseOutput
& (Join-Path $PSScriptRoot 'build-launcher.ps1') -OutputDirectory (Join-Path $delivery 'LauncherBuild')
Copy-Item -LiteralPath (Join-Path $delivery 'LauncherBuild\PerceptoX.exe') -Destination $output
& (Join-Path $PSScriptRoot 'verify-standalone.ps1') -PackageDirectory $output
$inventory = @(Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($output, $_.FullName); length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[ordered]@{ schema = 2; architecture = 'win-x64'; selfContained = $true; applicationPath = 'app'; launcher = 'PerceptoX.exe'; offlineRestore = [bool]$OfflineRestore;
    redistributionStatus = $manifest.redistributionStatus; files = $inventory } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'package-manifest.json') -Encoding utf8
$zip = Join-Path $delivery 'PerceptoX-win-x64.zip'
Compress-Archive -LiteralPath $output -DestinationPath $zip -CompressionLevel Optimal
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
& (Join-Path $PSScriptRoot 'package-sources.ps1') -DeliveryDirectory $delivery
& (Join-Path $PSScriptRoot 'package-third-party-sources.ps1') -DeliveryDirectory $delivery
$validation = Join-Path $delivery 'Validation'
New-Item -ItemType Directory -Path $validation | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-offline-package.ps1') -Destination $validation
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\OFFLINE_ACCEPTANCE.md') -Destination $validation
Write-Output "Package: $output"
Write-Output "ZIP: $zip"
Write-Warning 'Personal-use validation package: public redistribution licensing and clean-Windows gates are still open.'
