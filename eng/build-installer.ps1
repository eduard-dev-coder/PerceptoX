[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory, [string] $OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$allowed = Join-Path $taskRoot 'artifacts\distribution'
if (-not $package.StartsWith($allowed + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer requires a workspace distribution package.' }
& (Join-Path $PSScriptRoot 'verify-standalone.ps1') -PackageDirectory $package
$compiler = Join-Path $taskRoot 'tools\inno-setup-7.1.0\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Run eng/prepare-inno-tools.ps1 first.' }
& (Join-Path $PSScriptRoot 'validate-inno-languages.ps1')
$delivery = Split-Path $package -Parent
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $delivery 'Setup' }
if (-not $output.StartsWith($delivery + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer output must stay inside its delivery directory.' }
if (Test-Path -LiteralPath $output) { throw 'Installer output already exists; never overwrite a delivery.' }
New-Item -ItemType Directory -Path $output | Out-Null
$terms = Join-Path $output 'ThirdParty-terms.txt'
$builder = [Text.StringBuilder]::new()
$null = $builder.AppendLine('PerceptoX - third-party terms / licente si notificari terte')
$null = $builder.AppendLine('Dependencies retain their own licenses. This inventory does not constitute public redistribution clearance.')
$null = $builder.AppendLine("`r`n===== License inventory and open release gates =====`r`n" + [IO.File]::ReadAllText((Join-Path $package 'licenses\THIRD-PARTY-INVENTORY.json')))
$null = $builder.AppendLine("`r`n===== Inno Setup license =====`r`n" + [IO.File]::ReadAllText((Join-Path (Split-Path $compiler -Parent) 'license.txt')))
$files = @(Get-ChildItem -LiteralPath (Join-Path $package 'licenses') -Recurse -File | Where-Object { $_.Name -match 'license|notice|copying|mit-|apache-' -and $_.Extension -in @('.txt','.rtf','.json','') -and $_.Name -ne 'THIRD-PARTY-INVENTORY.json' })
$files += @(Get-Item -LiteralPath (Join-Path $package 'app\codecs\imagemagick\License.txt'),(Join-Path $package 'app\codecs\imagemagick\NOTICE.txt'))
foreach ($file in $files | Sort-Object FullName) {
    $null = $builder.AppendLine("`r`n===== " + [IO.Path]::GetRelativePath($package, $file.FullName) + " =====`r`n")
    if ($file.Extension -eq '.rtf') {
        Add-Type -AssemblyName System.Windows.Forms
        $reader = [Windows.Forms.RichTextBox]::new()
        try { $reader.Rtf = [IO.File]::ReadAllText($file.FullName); $null = $builder.AppendLine($reader.Text) }
        finally { $reader.Dispose() }
    } else { $null = $builder.AppendLine([IO.File]::ReadAllText($file.FullName)) }
}
[IO.File]::WriteAllText($terms, $builder.ToString(), [Text.UTF8Encoding]::new($false))
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'app\PerceptoX.WinUI.dll')).ProductVersion.Split('+')[0]
$compilerOutput = @(& $compiler ('/DPackageDir=' + $package) ('/DOutputDir=' + $output) ('/DTermsFile=' + $terms) ('/DAppVersion=' + $version) (Join-Path $PSScriptRoot 'installer\PerceptoX.iss') 2>&1)
$compilerExitCode = $LASTEXITCODE
$compilerOutput | Set-Content -LiteralPath (Join-Path $output 'compiler.log') -Encoding utf8
if ($compilerExitCode -ne 0) { $compilerOutput | Select-Object -Last 8 | Write-Output; throw 'Inno compilation failed. Partial artifacts retained for diagnosis.' }
foreach ($line in $compilerOutput | Where-Object { "$_" -match '^Warning:' }) { Write-Warning "$line" }
$setup = @(Get-ChildItem -LiteralPath $output -Filter '*Setup-win-x64.exe')
if ($setup.Count -ne 1) { throw 'Expected exactly one setup executable.' }
(Get-FileHash -LiteralPath $setup[0].FullName -Algorithm SHA256).Hash | Set-Content -LiteralPath ($setup[0].FullName + '.sha256') -Encoding ascii
Write-Output "Offline setup: $($setup[0].FullName)"
Write-Warning 'Unsigned validation build. Public redistribution and clean-Windows gates remain open.'
