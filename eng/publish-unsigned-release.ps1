[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DeliveryDirectory,
    [string] $Tag = 'v1.0.0',
    [string] $Commit = '9c96eabd06b3bf87357a263da4c9eb3a8c64442d'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$delivery = (Resolve-Path -LiteralPath $DeliveryDirectory).Path
$repository = 'eduard-dev-coder/PerceptoX'
$files = @(
    (Join-Path $delivery 'Setup\PerceptoX-1.0.0-Setup-win-x64.exe'),
    (Join-Path $delivery 'PerceptoX-win-x64.zip'),
    (Join-Path $delivery 'PerceptoX-sources.zip'),
    (Join-Path $delivery 'ThirdParty-sources\ImageMagick-7.1.2-26-Windows.7z'),
    (Join-Path $delivery 'Setup\ThirdParty-terms.txt'),
    (Join-Path $delivery 'ThirdParty-sources\source-provenance.json'),
    (Join-Path $delivery 'ThirdParty-sources\THIRD_PARTY_SOURCES.md')
)
foreach ($path in $files) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing asset: $path" } }
foreach ($path in $files[0..2]) {
    $expected = (Get-Content -LiteralPath ($path + '.sha256') -Raw).Trim()
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Checksum mismatch: $path" }
}
$checksumPath = Join-Path $delivery 'SHA256SUMS-release.txt'
if (Test-Path -LiteralPath $checksumPath) { throw 'Release checksum inventory already exists; do not overwrite.' }
$files | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) } | Set-Content -LiteralPath $checksumPath -Encoding ascii
$files += $checksumPath
# Reuse existing Git Credential Manager authorization, only in process memory.
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'
$credential = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
if ($LASTEXITCODE -ne 0) { throw 'GitHub credential unavailable. No release created.' }
$passwordLine = @($credential | Where-Object { $_.StartsWith('password=') })
if ($passwordLine.Count -ne 1) { throw 'GitHub credential unavailable.' }
$client = [Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.UserAgent.ParseAdd('PerceptoX-release/1.0')
$client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $passwordLine[0].Substring(9))
$client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
$client.Timeout = [TimeSpan]::FromMinutes(30)
$credential = $null; $passwordLine = $null
function Request-Json([string] $Method, [string] $Url, $Body) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $Url)
    try {
        if ($null -ne $Body) { $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 8), [Text.Encoding]::UTF8, 'application/json') }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) { throw "GitHub API returned HTTP $([int]$response.StatusCode)." }
            return $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
try {
    $existing = @(Request-Json 'GET' "https://api.github.com/repos/$repository/releases?per_page=100" $null)
    if ($existing | Where-Object tag_name -EQ $Tag) { throw 'Release already exists; do not overwrite or duplicate assets.' }
    $notes = @'
## PerceptoX 1.0.0 — Windows x64

Identify original photos from thumbnails, crops and screenshots, compare results side by side, group identical or near-identical photos, and copy selected images. Includes instant English/Romanian language switching and Light/Dark themes.

### Downloads
- **PerceptoX-1.0.0-Setup-win-x64.exe**: offline installer (recommended).
- **PerceptoX-win-x64.zip**: standalone folder distribution; extract completely and run PerceptoX.exe.
- **PerceptoX-sources.zip**: source snapshot packaged with this binary build. GitHub's automatic source archives represent the release tag; documentation/capture tooling were updated after the binary build.
- **ImageMagick-7.1.2-26-Windows.7z**: matching third-party source archive, for developers; not needed to run the application.
- **ThirdParty-terms.txt**, provenance and source instructions: dependency notices and corresponding-source information.
- **SHA256SUMS-release.txt**: SHA-256 inventory of the uploaded assets.

### Signing status
**This release is unsigned.** Windows may show an unknown-publisher or SmartScreen warning. Trusted signing is planned after provider approval. Do not disable Windows security protections globally.

### Standalone operation
.NET/WinUI, SQLite and the offline codec bundle are included. Optional codec activation requires user consent. No Ollama or other AI software is required. The maintainer reports successful operation on a clean Windows installation.

Own source: GPL-3.0; bundled dependencies retain their respective terms. Image similarity scores are heuristic, not probabilities. Inspect matches before taking file actions and retain backups.

Website: https://eduard-dev-coder.github.io/PerceptoX/
'@
    $release = Request-Json 'POST' "https://api.github.com/repos/$repository/releases" @{tag_name=$Tag; target_commitish=$Commit; name='PerceptoX 1.0.0'; body=$notes; draft=$true; prerelease=$false}
    Write-Output "Draft created: $($release.html_url)"
    foreach ($path in $files) {
        $name = [IO.Path]::GetFileName($path)
        Write-Output "Uploading: $name"
        $stream = [IO.File]::OpenRead($path)
        $content = [Net.Http.StreamContent]::new($stream)
        $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
        try {
            $url = "https://uploads.github.com/repos/$repository/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($name))"
            $response = $client.PostAsync($url, $content).GetAwaiter().GetResult()
            try { if (-not $response.IsSuccessStatusCode) { throw "Asset upload failed with HTTP $([int]$response.StatusCode); draft retained." } }
            finally { $response.Dispose() }
        } finally { $content.Dispose(); $stream.Dispose() }
    }
    $check = Request-Json 'GET' "https://api.github.com/repos/$repository/releases/$($release.id)" $null
    foreach ($path in $files) {
        $asset = @($check.assets | Where-Object name -EQ ([IO.Path]::GetFileName($path)))
        if ($asset.Count -ne 1 -or $asset[0].size -ne (Get-Item -LiteralPath $path).Length -or $asset[0].state -ne 'uploaded') { throw 'Remote asset inventory mismatch; draft retained.' }
    }
    $published = Request-Json 'PATCH' "https://api.github.com/repos/$repository/releases/$($release.id)" @{draft=$false}
    Write-Output "Published: $($published.html_url)"
} finally { $client.Dispose() }
