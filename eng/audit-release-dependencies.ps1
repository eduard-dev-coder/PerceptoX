[CmdletBinding()]
param([Parameter(Mandatory)][string] $ReportDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory = [IO.Path]::GetFullPath($ReportDirectory)
if (-not $directory.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Audit reports must remain inside the workspace.' }
if (Test-Path -LiteralPath $directory) { throw 'Use a new audit report directory; existing reports are not overwritten.' }
$output = & (Join-Path $PSScriptRoot 'dotnet-sandbox.ps1') -DotNetArguments @('list', (Join-Path $root 'PerceptoX.sln'), 'package', '--vulnerable', '--include-transitive', '--format', 'json', '--no-restore')
if ($LASTEXITCODE -ne 0) { throw 'Online NuGet advisory query failed; no successful audit is recorded.' }
$report = ($output -join [Environment]::NewLine) | ConvertFrom-Json
if (@($report.projects).Count -eq 0 -or @($report.sources).Count -eq 0) { throw 'Empty NuGet advisory report rejected.' }
foreach ($project in $report.projects) { $project.path = [IO.Path]::GetRelativePath($root, $project.path) }
New-Item -ItemType Directory -Path $directory | Out-Null
[ordered]@{ utc = [DateTime]::UtcNow.ToString('o'); scope = 'Known NuGet advisories only; native DLLs and unknown vulnerabilities not certified'; result = $report } |
    ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $directory 'nuget-advisories.json') -Encoding utf8
Write-Output "NuGet advisory report: $directory"
