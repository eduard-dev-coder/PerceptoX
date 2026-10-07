[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$demoRoot = Join-Path $projectRoot 'artifacts\demo'
$originals = Join-Path $demoRoot 'Originals'
New-Item -ItemType Directory -Path $originals -Force | Out-Null
$inputPath = Join-Path $originals 'Earth-Blue-Marble.jpg'
$inputUrl = 'https://assets.science.nasa.gov/dynamicimage/assets/science/esd/climate/2023/12/ImageWall5_1920x1200-80.jpg?crop=faces%2Cfocalpoint&fit=clip&h=1200&w=1920'
if (-not (Test-Path -LiteralPath $inputPath)) { Invoke-WebRequest $inputUrl -OutFile $inputPath -TimeoutSec 60 }
$application = Join-Path $projectRoot 'src\PerceptoX.WinUI\bin\x64\Debug\net10.0-windows10.0.19041.0\PerceptoX.WinUI.exe'
if (-not (Test-Path -LiteralPath $application)) { throw 'Build WinUI Debug x64 first.' }
$captureRoot = Join-Path $projectRoot ('artifacts\documentation-captures\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$assets = Join-Path $projectRoot 'website\assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
foreach ($theme in @('Light','Dark')) {
    $destination = Join-Path $captureRoot $theme
    $captureArguments = @('--design-preview="' + $destination + '"', '--documentation-demo-root="' + $demoRoot + '"', '--design-preview-language=en', '--theme=' + $theme.ToLowerInvariant())
    $captureProcess = Start-Process -FilePath $application -ArgumentList $captureArguments -PassThru -WindowStyle Hidden
    if (-not $captureProcess.WaitForExit(120000)) { $captureProcess.Kill(); throw 'Documentation capture exceeded two minutes.' }
    $errorPath = Join-Path $destination 'capture-error.txt'
    if (Test-Path -LiteralPath $errorPath) { throw ([IO.File]::ReadAllText($errorPath)) }
    foreach ($page in @('identify','comparison','scan','groups','settings')) {
        $image = Join-Path $destination ($page + '\preview.png')
        if (-not (Test-Path -LiteralPath $image)) { throw "Missing actual capture: $theme/$page" }
        Copy-Item -LiteralPath $image -Destination (Join-Path $assets ($page + '-' + $theme.ToLowerInvariant() + '-en.png')) -Force
    }
    Copy-Item -LiteralPath (Join-Path $destination 'capture-provenance.json') -Destination (Join-Path $assets ('capture-' + $theme.ToLowerInvariant() + '-en.json')) -Force
}
Write-Output "Real engine captures: $captureRoot"
