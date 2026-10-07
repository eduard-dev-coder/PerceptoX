[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Filter,
    [switch] $InProcess
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$benchmarkDll = Join-Path $repositoryRoot 'benchmarks\PerceptoX.Benchmarks\bin\Release\net10.0\PerceptoX.Benchmarks.dll'
if (-not (Test-Path -LiteralPath $benchmarkDll -PathType Leaf)) {
    throw 'Build the benchmark project in Release before running it.'
}

$priorNuGetAudit = [Environment]::GetEnvironmentVariable('NuGetAudit', 'Process')
$result = 1
try {
    # BenchmarkDotNet restores a generated project. Package restore/audit is run separately.
    $env:NuGetAudit = 'false'
    # Source ZIP deliveries contain other .csproj files with the same name.
    # In-process runs avoid BenchmarkDotNet's ambiguous recursive project discovery.
    $taskBenchmarkArguments = @($benchmarkDll, '--filter', $Filter)
    if ($InProcess) { $taskBenchmarkArguments += '-i' }
    & (Join-Path $PSScriptRoot 'dotnet-sandbox.ps1') -DotNetArguments $taskBenchmarkArguments
    $result = $LASTEXITCODE
}
finally {
    if ($null -eq $priorNuGetAudit) {
        Remove-Item Env:NuGetAudit -ErrorAction SilentlyContinue
    }
    else {
        $env:NuGetAudit = $priorNuGetAudit
    }
}

exit $result
