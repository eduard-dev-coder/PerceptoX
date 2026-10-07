[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ro = Get-Content (Join-Path $taskRoot 'languages/ro.json') -Raw | ConvertFrom-Json -AsHashtable
$en = Get-Content (Join-Path $taskRoot 'languages/en.json') -Raw | ConvertFrom-Json -AsHashtable
if ($ro.schemaVersion -ne 1 -or $en.schemaVersion -ne 1 -or $ro.language -ne 'ro' -or $en.language -ne 'en') { throw 'Invalid builtin schema.' }
if (@(Compare-Object @($ro.strings.Keys) @($en.strings.Keys)).Count) { throw 'Builtin key mismatch.' }
foreach ($status in @('InsufficientData', 'Rejected', 'Validated')) {
    if (-not $ro.strings.Contains('Calibration.Status.' + $status)) { throw "Missing calibration status: $status" }
}
foreach ($key in $ro.strings.Keys) {
    $a = [Text.CompositeFormat]::Parse($ro.strings[$key]); $b = [Text.CompositeFormat]::Parse($en.strings[$key])
    if ($a.MinimumArgumentCount -ne $b.MinimumArgumentCount) { throw "Placeholder mismatch: $key" }
    if ($en.strings[$key] -match '[ăâîșțĂÂÎȘȚ]') { throw "Untranslated English entry: $key" }
}
foreach ($file in Get-ChildItem (Join-Path $taskRoot 'src') -Recurse -File | Where-Object { $_.Extension -in @('.cs','.xaml') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
    $source = [IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($source, 'UiText\.T\("(?<key>[^"{}]+)"\s*[,)]|loc:UiTextExtension Key=(?<key>[^}]+)')) {
        if (-not $ro.strings.Contains($match.Groups['key'].Value)) { throw "Missing source key: $($match.Groups['key'].Value)" }
    }
}
Write-Output ('Language catalogs and UI references verified: {0} keys, ro/en, placeholder parity.' -f $ro.strings.Count)
