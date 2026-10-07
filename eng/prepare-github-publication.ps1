[CmdletBinding()]
param([string] $BinaryDeliveryDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = 'github-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$delivery = Join-Path $projectRoot ('artifacts\distribution\' + $runId)
New-Item -ItemType Directory -Path $delivery | Out-Null
$assets = Join-Path $projectRoot 'website\assets'
foreach ($capture in @('identify-light-en.webp','comparison-light-en.webp','groups-dark-en.webp','settings-light-en.webp')) {
    if (-not (Test-Path -LiteralPath (Join-Path $assets $capture))) { throw 'Capture real documentation screenshots before preparing publication.' }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'graphics\icon.png') -Destination (Join-Path $assets 'icon.png') -Force
& (Join-Path $PSScriptRoot 'package-sources.ps1') -DeliveryDirectory $delivery
$snapshot = Join-Path $delivery 'PerceptoX-sources'
# Print only filename/line for likely credentials, never the matched secret value.
$sensitivePatterns = @('gh[pousr]_[A-Za-z0-9]{30,}', 'github_pat_[A-Za-z0-9_]{50,}', 'sk-(proj-)?[A-Za-z0-9_-]{35,}', '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----')
$textExtensions = @('.cs','.cpp','.h','.rc','.ps1','.xaml','.xml','.json','.md','.txt','.yml','.yaml','.props','.csproj','.targets','.html','.js','.css','.iss','.isl','.sln','.pubxml','.manifest')
$findings = @(Get-ChildItem -LiteralPath $snapshot -Recurse -Force -File | Where-Object Extension -in $textExtensions |
    Select-String -Pattern $sensitivePatterns | ForEach-Object { [ordered]@{ Path = [IO.Path]::GetRelativePath($snapshot,$_.Path); Line = $_.LineNumber } })
if ($findings.Count -gt 0) { $findings | ConvertTo-Json; throw 'Potential credential detected; do not publish this snapshot.' }
$inventory = @(Get-ChildItem -LiteralPath $snapshot -Recurse -Force -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($snapshot,$_.FullName).Replace('\','/'); length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $delivery 'source-inventory.json') -Encoding utf8
$sourceZip = Join-Path $delivery 'PerceptoX-sources.zip'
$checksumLines = @((Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash + '  PerceptoX-sources.zip')
if ($BinaryDeliveryDirectory) {
    $binaryDelivery = (Resolve-Path -LiteralPath $BinaryDeliveryDirectory).Path
    foreach ($relative in @('PerceptoX-win-x64.zip','Setup\PerceptoX-1.0.0-Setup-win-x64.exe','ThirdParty-sources\ImageMagick-7.1.2-26-Windows.7z')) {
        $candidate = Join-Path $binaryDelivery $relative
        if (-not (Test-Path -LiteralPath $candidate)) { throw "Missing release input: $relative" }
        $checksumLines += (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($candidate)
    }
}
$checksumLines | Set-Content -LiteralPath (Join-Path $delivery 'SHA256SUMS.txt') -Encoding ascii
[ordered]@{ Schema=1; Repository='eduard-dev-coder/PerceptoX'; Snapshot=$snapshot; SourceFiles=$inventory.Count;
    SecretScan='No matches in the bounded text-pattern scan; not a complete security audit'; BinaryRedistribution='Pending existing gates';
    BinaryDelivery=$BinaryDeliveryDirectory; SourceZip=$sourceZip; CreatedAtUtc=[DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $delivery 'publication-preparation.json') -Encoding utf8
Write-Output "Publication snapshot: $snapshot ($($inventory.Count) files)"
Write-Output "Release material: $delivery"
