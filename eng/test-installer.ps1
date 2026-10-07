[CmdletBinding()]
param([Parameter(Mandatory)][string] $SetupPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$distribution = Join-Path $taskRoot 'artifacts\distribution'
if (-not $setup.StartsWith($distribution + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Only workspace delivery installers may be tested.' }
$validationRoot = Join-Path $taskRoot ('artifacts\validation\setup-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
if (Test-Path -LiteralPath $validationRoot) { throw 'Validation directory already exists.' }
New-Item -ItemType Directory -Path $validationRoot | Out-Null
$installed = Join-Path $validationRoot 'Installed app with spaces'
$rejected = Join-Path $validationRoot 'Refused without consent'
$uninstallRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{EDFF7E2D-24BA-4B25-B20C-981B7623C438}_is1'
$hadRegistry = Test-Path -LiteralPath $uninstallRegistry
$machinePath = [Environment]::GetEnvironmentVariable('PATH','Machine')
$userPath = [Environment]::GetEnvironmentVariable('PATH','User')
$report = [ordered]@{ utc=[DateTime]::UtcNow.ToString('o'); setup=$setup; setupSha256=(Get-FileHash $setup -Algorithm SHA256).Hash; scope='Isolated validation mode: no shortcuts or uninstall registration; development machine, not clean Windows'; status='Failed' }
function Invoke-OwnedProcess([string] $executable, [string[]] $arguments) {
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'Owned validation process exceeded three minutes.' }
        return $process.ExitCode
    } finally { $process.Dispose() }
}
try {
    $common = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS','/VALIDATIONMODE=YES','/LANG=ro')
    $exit = Invoke-OwnedProcess $setup ($common + @(('/DIR="' + $rejected + '"'),('/LOG="' + (Join-Path $validationRoot 'no-consent.log') + '"')))
    if ($exit -eq 0 -or (Test-Path -LiteralPath (Join-Path $rejected 'PerceptoX.exe'))) { throw 'Silent install unexpectedly accepted missing license consent.' }
    $report['withoutConsentExitCode'] = $exit
    $exit = Invoke-OwnedProcess $setup ($common + @('/ACCEPTLICENSES=YES','/COMPONENTS=app,codecs','/TASKS=',('/DIR="' + $installed + '"'),('/LOG="' + (Join-Path $validationRoot 'install.log') + '"')))
    if ($exit -ne 0) { throw "Offline installer failed: $exit. Inspect install.log." }
    $report['acceptedInstallExitCode'] = $exit
    & (Join-Path $PSScriptRoot 'verify-standalone.ps1') -PackageDirectory $installed
    $activePointer = Join-Path $installed 'app\modules\installed\active.json'
    $active = Get-Content -LiteralPath $activePointer -Raw | ConvertFrom-Json
    $slotGuid = [Guid]::Empty
    if (-not [Guid]::TryParseExact($active.slot, 'N', [ref]$slotGuid)) { throw 'Invalid installed codec slot.' }
    $slot = Join-Path $installed ('app\modules\installed\slot-' + $active.slot)
    if (-not (Test-Path -LiteralPath (Join-Path $slot 'magick.exe'))) { throw 'Active offline codec missing.' }
    $report['codecVersion'] = $active.version
    $pointerBefore = (Get-FileHash -LiteralPath $activePointer -Algorithm SHA256).Hash
    $exit = Invoke-OwnedProcess (Join-Path $installed 'PerceptoX.exe') @('--install-offline-codecs')
    if ($exit -ne 2 -or (Get-FileHash -LiteralPath $activePointer -Algorithm SHA256).Hash -ne $pointerBefore) { throw 'Launcher/codec CLI consent gate failed.' }
    $report['codecWithoutConsentExitCode'] = $exit
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-offline-package.ps1') -PackageDirectory $installed -ResultDirectory (Join-Path $validationRoot 'startup')
    if ($LASTEXITCODE -ne 0) { throw 'Installed application startup failed.' }
    # Unknown files are not installer-owned. Prove that uninstall does not recursively erase them.
    $personalMarker = Join-Path $installed 'personal-file-must-survive.txt'
    $outsideMarker = Join-Path $validationRoot 'photo-library-must-survive.txt'
    [IO.File]::WriteAllText($personalMarker, 'Unregistered personal file - preserve')
    [IO.File]::WriteAllText($outsideMarker, 'Library outside install - preserve')
    $uninstaller = (Resolve-Path -LiteralPath (Join-Path $installed 'unins000.exe')).Path
    if (-not $uninstaller.StartsWith($validationRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Uninstall path escaped validation directory.' }
    $exit = Invoke-OwnedProcess $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $validationRoot 'uninstall.log') + '"'))
    if ($exit -ne 0) { throw "Validation uninstall failed: $exit" }
    for ($attempt=0; $attempt -lt 40 -and (Test-Path -LiteralPath (Join-Path $installed 'PerceptoX.exe')); $attempt++) { Start-Sleep -Milliseconds 250 }
    if (Test-Path -LiteralPath (Join-Path $installed 'PerceptoX.exe')) { throw 'Uninstall did not remove installer-owned launcher.' }
    if (-not (Test-Path -LiteralPath $personalMarker) -or -not (Test-Path -LiteralPath $outsideMarker)) { throw 'Uninstall removed personal data.' }
    if ((Test-Path -LiteralPath $uninstallRegistry) -ne $hadRegistry) { throw 'Validation installer changed uninstall registration.' }
    if ([Environment]::GetEnvironmentVariable('PATH','Machine') -ne $machinePath -or [Environment]::GetEnvironmentVariable('PATH','User') -ne $userPath) { throw 'Installer changed PATH.' }
    $report['uninstallExitCode'] = $exit
    $report['personalFilesPreserved'] = $true
    $report['pathUnchanged'] = $true
    $report['uninstallRegistrationUnchanged'] = $true
    $report.status = 'Passed'
} catch { $report['error'] = $_.Exception.Message; throw }
finally {
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $validationRoot 'setup-result.json') -Encoding utf8
    Write-Output "Installer validation report: $validationRoot"
}
