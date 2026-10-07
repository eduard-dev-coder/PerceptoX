[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($PackageDirectory)
$application = if (Test-Path -LiteralPath (Join-Path $output 'app\PerceptoX.WinUI.exe')) { Join-Path $output 'app' } else { $output }
if ($application -ne $output -and -not (Test-Path -LiteralPath (Join-Path $output 'PerceptoX.exe'))) { throw 'Root launcher missing.' }
$required = @('PerceptoX.WinUI.exe', 'PerceptoX.WinUI.dll', 'PerceptoX.WinUI.runtimeconfig.json',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'e_sqlite3.dll', 'Microsoft.ui.xaml.dll',
    'Microsoft.WindowsAppRuntime.dll', 'Microsoft.Internal.FrameworkUdk.dll', 'Microsoft.Graphics.Canvas.dll', 'PerceptoX.WinUI.pri',
    'App.xbf', 'MainWindow.xbf', 'Pages\BatchMatchPage.xbf', 'Pages\IndexingPage.xbf',
    'Pages\SearchPage.xbf', 'Pages\SettingsPage.xbf', 'Styles\PerceptoX.Theme.xbf',
    'graphics\PerceptoX.ico', 'graphics\icon.png', 'graphics\logo.png', 'graphics\header-illustration.png',
    'codecs\imagemagick\magick.exe', 'codecs\imagemagick\License.txt', 'codecs\imagemagick\NOTICE.txt',
    'languages\ro.json', 'languages\en.json')
foreach ($name in $required) {
    $path = Join-Path $application $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Missing packaged dependency: $name" }
}
if (-not (Test-Path -LiteralPath (Join-Path $output 'licenses\THIRD-PARTY-INVENTORY.json'))) { throw 'Third-party inventory missing.' }
$config = Get-Content -LiteralPath (Join-Path $application 'PerceptoX.WinUI.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or $config.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') { throw 'Framework-dependent .NET output rejected.' }
if ($config.runtimeOptions.PSObject.Properties.Name -notcontains 'includedFrameworks') { throw 'Self-contained runtime metadata missing.' }
$runtime = Join-Path $application 'codecs\imagemagick'
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $runtime 'magick.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.WorkingDirectory = $runtime
$start.ArgumentList.Add('-list'); $start.ArgumentList.Add('format')
$start.Environment['PATH'] = $runtime + [IO.Path]::PathSeparator + [Environment]::GetFolderPath('System')
$start.Environment['MAGICK_HOME'] = $runtime
$start.Environment['MAGICK_CONFIGURE_PATH'] = $runtime
$start.Environment['MAGICK_CODER_MODULE_PATH'] = Join-Path $runtime 'modules\coders'
$start.Environment['MAGICK_FILTER_MODULE_PATH'] = Join-Path $runtime 'modules\filters'
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw 'Bundled codec probe timed out.' }
    $formats = $stdout.GetAwaiter().GetResult(); $errorText = $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "Bundled codec failed: $errorText" }
    foreach ($format in @('HEIC', 'HEIF', 'AVIF')) {
        if ($formats -notmatch "(?m)^\s*$format\*?\s+\S+\s+r") { throw "Bundled codec cannot read $format" }
    }
} finally { $process.Dispose() }
if (Test-Path -LiteralPath (Join-Path $output 'package-manifest.json')) {
    $manifest = Get-Content -LiteralPath (Join-Path $output 'package-manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $output $entry.path))
        if (-not $path.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package inventory escaped root.' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Package file changed: $($entry.path)" }
    }
}
Write-Output 'Standalone structure, .NET metadata, bundled codec declarations and existing package hashes verified. Not a clean-Windows certification.'
