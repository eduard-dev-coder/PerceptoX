[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolRoot = Join-Path $taskRoot 'tools\inno-setup-7.1.0'
$downloadRoot = Join-Path $taskRoot 'tools\downloads'
$installer = Join-Path $downloadRoot 'innosetup-7.1.0-x64.exe'
$expectedHash = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $installer)) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $installer
}
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Inno installer hash differs from official GitHub release.' }
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') { throw 'Inno installer signature is not valid or has an unexpected publisher.' }
if (-not (Test-Path -LiteralPath (Join-Path $toolRoot 'ISCC.exe'))) {
    # Official portable mode: no registry, associations, shortcuts or uninstaller entries.
    $toolArgs = @('/PORTABLE=1','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $toolRoot + '"'))
    $process = Start-Process -FilePath $installer -ArgumentList $toolArgs -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Portable Inno preparation failed: $($process.ExitCode)" }
}
if (-not (Test-Path -LiteralPath (Join-Path $toolRoot 'ISCC.exe'))) { throw 'Inno compiler missing.' }
$romanian = Join-Path $toolRoot 'Languages\Romanian.isl'
$romanianHash = '0DA808AF7190F59A72B1EF41E071E43EE33029492AE9A3629F7D99E2D48F6073'
if (-not (Test-Path -LiteralPath $romanian)) {
    # Inno's official source tag includes this user-contributed translation, but its binary installer does not.
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/jrsoftware/issrc/is-7_1_0/Files/Languages/Unofficial/Romanian.isl' -OutFile $romanian
}
if ((Get-FileHash -LiteralPath $romanian -Algorithm SHA256).Hash -ne $romanianHash) { throw 'Romanian Inno translation hash mismatch.' }
$compatibleRomanian = Join-Path $toolRoot 'Languages\Romanian-PerceptoX.isl'
$translation = [IO.File]::ReadAllLines($romanian) | Where-Object { $_ -notmatch '^(DownloadingLabel|ErrorFileHash1|ErrorFileHash2)=' }
# Mechanical compatibility transformation: keep original, verified translation and all credits intact.
[IO.File]::WriteAllLines($compatibleRomanian, @('; Modified by PerceptoX: only three obsolete Inno 6 message keys removed for Inno 7.1.0.') + $translation, [Text.UTF8Encoding]::new($false))
Write-Output "Verified portable Inno compiler: $toolRoot"
