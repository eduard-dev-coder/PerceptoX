[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compilerRoot = Join-Path $taskRoot 'tools\inno-setup-7.1.0'
function Read-Messages([string[]] $paths) {
    $messages = @{}
    foreach ($path in $paths) {
        $inMessages = $false
        foreach ($line in [IO.File]::ReadAllLines($path)) {
            if ($line -match '^\[([^]]+)\]') { $inMessages = $Matches[1] -eq 'Messages'; continue }
            if ($inMessages -and $line -match '^([^;=]+)=(.*)$') { $messages[$Matches[1]] = $Matches[2] }
        }
    }
    return $messages
}
$english = Read-Messages @((Join-Path $compilerRoot 'Default.isl'))
$romanian = Read-Messages @((Join-Path $compilerRoot 'Languages\Romanian-PerceptoX.isl'),(Join-Path $PSScriptRoot 'installer\Messages.ro.isl'))
foreach ($key in $english.Keys) {
    if (-not $romanian.ContainsKey($key)) { throw "Romanian setup message missing: $key" }
    $expected = @([regex]::Matches($english[$key], '%[1-9][0-9]*|%n') | ForEach-Object Value | Sort-Object)
    $actual = @([regex]::Matches($romanian[$key], '%[1-9][0-9]*|%n') | ForEach-Object Value | Sort-Object)
    if (($expected -join '|') -ne ($actual -join '|')) { throw "Romanian setup placeholders differ: $key" }
}
foreach ($key in $romanian.Keys) { if (-not $english.ContainsKey($key)) { throw "Unsupported Romanian setup message: $key" } }
Write-Output "Installer Romanian/English messages and placeholders verified: $($english.Count) messages."
