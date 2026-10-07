[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = '2.102.0'
$name = "gh_${version}_windows_amd64.zip"
$directory = Join-Path $projectRoot 'tools\downloads'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$destination = Join-Path $directory $name
$releaseUrl = "https://github.com/cli/cli/releases/download/v$version"
$checksums = (Invoke-WebRequest "$releaseUrl/gh_${version}_checksums.txt" -TimeoutSec 30).Content
$line = @($checksums -split "`n" | Where-Object { $_.Trim().EndsWith('  ' + $name, [StringComparison]::Ordinal) })
if ($line.Count -ne 1 -or $line[0] -notmatch '^([a-fA-F0-9]{64})\s+') { throw 'Official GitHub CLI checksum not found.' }
$expected = $Matches[1]
if (-not (Test-Path -LiteralPath $destination)) { Invoke-WebRequest "$releaseUrl/$name" -OutFile $destination -TimeoutSec 120 }
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) { throw 'GitHub CLI download checksum mismatch; do not execute.' }
$target = Join-Path $directory "gh_${version}_windows_amd64"
if (-not (Test-Path -LiteralPath $target)) { Expand-Archive -LiteralPath $destination -DestinationPath $target }
$executable = Join-Path $target 'bin\gh.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'GitHub CLI executable missing from verified archive.' }
Write-Output "Verified portable GitHub CLI: $executable"
Write-Output 'This is a maintainer tool, not an end-user dependency. It does not sign in or store credentials automatically.'
