[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory, [string] $LicenseDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($PackageDirectory)
$licenses = if ($LicenseDirectory) { [IO.Path]::GetFullPath($LicenseDirectory) } else { Join-Path $output 'licenses' }
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses\Apache-2.0.txt') -Destination $licenses
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses\MIT-dotnet.txt') -Destination $licenses
$deps = Get-Content -LiteralPath (Join-Path $output 'PerceptoX.WinUI.deps.json') -Raw | ConvertFrom-Json
$inventory = @()
$libraries = @($deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' })
# Native/build-only WinAppSDK packages can binplace DLLs without appearing in deps.json.
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'src\PerceptoX.WinUI\obj\project.assets.json') -Raw | ConvertFrom-Json
$known = @{}
foreach ($library in $libraries) { $known[$library.Name] = $true }
foreach ($library in $assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Microsoft.WindowsAppSDK.*/*' }) {
    if (-not $known.ContainsKey($library.Name)) { $libraries += $library }
}
foreach ($library in $libraries) {
    $packageRoot = Join-Path $projectRoot (Join-Path '.packages' $library.Value.path)
    $nuspec = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec' -File)
    if ($nuspec.Count -ne 1) { throw "Missing package metadata: $($library.Name)" }
    [xml]$metadata = Get-Content -LiteralPath $nuspec[0].FullName -Raw
    $license = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    $legacyWin2D = $null -eq $license -and $library.Name -eq 'Microsoft.Graphics.Win2D/1.4.0'
    $licenseEvidence = $null
    if ($legacyWin2D) {
        $binarySourceHash = (Get-Content -LiteralPath (Join-Path $packageRoot 'Win2d.githash.txt') -Raw).Trim()
        if ($binarySourceHash -ne '57e06e2c24703526f500035129251d063881a44d') { throw 'Unreviewed Win2D binary source hash.' }
        $legacyUrl = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='licenseUrl']").InnerText
        if ($legacyUrl -ne 'http://www.microsoft.com/web/webpi/eula/eula_win2d_10012014.htm') { throw 'Unreviewed Win2D legacy license URL.' }
        $licenseType = 'legacy-url-with-upstream-source-notice'
        $declaredLicense = $legacyUrl
        $appliedLicense = 'Upstream Win2D MIT source notice; legacy NuGet binary terms reconciliation pending before public redistribution'
        $licenseEvidence = [ordered]@{
            upstreamLicense = 'https://raw.githubusercontent.com/microsoft/Win2D/0fd4f810be5bf0cb9c981432a6f4ab3954fda08f/LICENSE.txt'
            upstreamReleaseHistory = 'https://github.com/microsoft/Win2D/blob/winappsdk/main/CHANGELOG.md'
            binarySourceHash = $binarySourceHash
            note = 'Original metadata preserved. Legacy URL redirects to unrelated documentation; packaged source hash is not available in the public repository. Do not interpret this inventory as binary redistribution clearance.'
        }
    } elseif ($null -eq $license) { throw "License declaration missing: $($library.Name)" }
    else {
        $licenseType = $license.GetAttribute('type')
        $declaredLicense = $license.InnerText
        $appliedLicense = if ($library.Name -eq 'SixLabors.ImageSharp/3.1.12') { 'Apache-2.0; grant for use in open-source GPL-3.0-only PerceptoX' } else { $license.InnerText }
    }
    $copyright = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='copyright']")
    $attribution = if ($copyright) { $copyright.InnerText } else { $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='authors']").InnerText }
    $destination = Join-Path $licenses ($library.Name.Replace('/', '-'))
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $nuspec[0].FullName -Destination $destination
    foreach ($notice in Get-ChildItem -LiteralPath $packageRoot -File | Where-Object Name -Match 'license|notice|copying') {
        Copy-Item -LiteralPath $notice.FullName -Destination $destination
    }
    if ($legacyWin2D) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses\MIT-Win2D.txt') -Destination $destination
        $licenseEvidence | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'LICENSE-PROVENANCE.json') -Encoding utf8
    } elseif ($licenseType -eq 'file') {
        $licensePath = [IO.Path]::GetFullPath((Join-Path $packageRoot $license.InnerText))
        if (-not $licensePath.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'License path escaped package.' }
        Copy-Item -LiteralPath $licensePath -Destination $destination -Force
    } elseif ($license.InnerText -notin @('MIT', 'Apache-2.0')) {
        throw "Unreviewed license expression: $($library.Name) $($license.InnerText)"
    }
    $scope = if ($known.ContainsKey($library.Name)) { 'Published runtime dependency' } else { 'WinAppSDK native/build dependency; conservative notice coverage' }
    $inventory += [ordered]@{ package = $library.Name; declaredLicense = $declaredLicense; appliedLicense = $appliedLicense; licenseType = $licenseType; attribution = $attribution; scope = $scope; licenseEvidence = $licenseEvidence }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $output 'PerceptoX.WinUI.runtimeconfig.json') -Raw | ConvertFrom-Json
$runtimeVersion = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -EQ 'Microsoft.NETCore.App')[0].version
$runtimePackage = Join-Path $projectRoot ".packages\microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
$runtimeDestination = Join-Path $licenses "dotnet-runtime-$runtimeVersion"
New-Item -ItemType Directory -Path $runtimeDestination -Force | Out-Null
foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) { Copy-Item -LiteralPath (Join-Path $runtimePackage $name) -Destination $runtimeDestination }
[ordered]@{ generatedFrom = 'Published runtime and WinAppSDK native/build dependency metadata; not a legal clearance'; packages = $inventory;
    pending = @('ImageMagick delegates: full source/build/relink compliance review before redistribution', 'Microsoft runtime redistribution terms and recipient terms review', 'Win2D 1.4.0 legacy NuGet license URL reconciliation with upstream MIT source notice', 'Graphic rights and complete GPL distribution compatibility') } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $licenses 'THIRD-PARTY-INVENTORY.json') -Encoding utf8
