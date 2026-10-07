[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory, [string] $VisualStudioRoot, [string] $WindowsSdkRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Launcher output must stay in workspace.' }
if (-not $VisualStudioRoot) {
    $discovery = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $discovery)) { throw 'Visual Studio discovery unavailable; specify -VisualStudioRoot.' }
    $VisualStudioRoot = & $discovery -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or -not $VisualStudioRoot) { throw 'Install compatible x64 C++ Build Tools or specify -VisualStudioRoot.' }
}
$nativeRoot = Join-Path $VisualStudioRoot 'VC\Tools\MSVC'
$nativeVersion = Get-ChildItem -LiteralPath $nativeRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $nativeVersion.FullName 'bin\Hostx64\x64\cl.exe'
$sdkRoot = if ($WindowsSdkRoot) { $WindowsSdkRoot } else { Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10' }
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object Name -like '10.*' | Sort-Object Name -Descending | Select-Object -First 1
$resourceCompiler = Join-Path $sdkRoot ('bin\' + $sdkVersion.Name + '\x64\rc.exe')
foreach ($requiredTool in @($compiler,$resourceCompiler)) {
    if (-not (Test-Path -LiteralPath $requiredTool -PathType Leaf)) { throw "Missing build tool: $requiredTool" }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'graphics\PerceptoX.ico') -Destination $output
$previousInclude = $env:INCLUDE
$previousLib = $env:LIB
$env:INCLUDE = (Join-Path $nativeVersion.FullName 'include') + ';' + ((@('ucrt','shared','um') | ForEach-Object { Join-Path $sdkVersion.FullName $_ }) -join ';')
$env:LIB = (Join-Path $nativeVersion.FullName 'lib\x64') + ';' + ((@('ucrt','um') | ForEach-Object { Join-Path $sdkRoot ('Lib\' + $sdkVersion.Name + '\' + $_ + '\x64') }) -join ';')
Push-Location $output
try {
    & $resourceCompiler /nologo ('/fo' + (Join-Path $output 'launcher.res')) (Join-Path $PSScriptRoot 'launcher\launcher.rc')
    if ($LASTEXITCODE -ne 0) { throw 'Launcher resource compilation failed.' }
    & $compiler /nologo /O2 /MT /EHsc /W4 /WX /std:c++17 (Join-Path $PSScriptRoot 'launcher\launcher.cpp') ('/Fe:' + (Join-Path $output 'PerceptoX.exe')) ('/Fo:' + (Join-Path $output 'launcher.obj')) (Join-Path $output 'launcher.res') /link /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /HIGHENTROPYVA kernel32.lib user32.lib
    if ($LASTEXITCODE -ne 0) { throw 'Native launcher compilation failed.' }
} finally { Pop-Location; $env:INCLUDE = $previousInclude; $env:LIB = $previousLib }
Write-Output "Native launcher: $output\PerceptoX.exe"
