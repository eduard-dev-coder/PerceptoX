[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $DotNetArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$sandboxProfile = Join-Path $repositoryRoot '.sandbox-profile'

$env:APPDATA = Join-Path $sandboxProfile 'AppData\Roaming'
$env:DOTNET_CLI_HOME = Join-Path $sandboxProfile 'dotnet'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $sandboxProfile 'NuGet\http-cache'
# Keep test fixtures in the writable workspace, not Windows' virtualized sandbox
# temp folder where directory rename operations can be denied. Process-local only.
$env:TEMP = Join-Path $sandboxProfile 'Temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'

New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME -Force | Out-Null
New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null
New-Item -ItemType Directory -Path $env:NUGET_HTTP_CACHE_PATH -Force | Out-Null
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null

& dotnet @DotNetArguments
exit $LASTEXITCODE
