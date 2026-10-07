# Compatible with built-in Windows PowerShell 5.1; no SDK/PowerShell 7 required.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [string] $ResultDirectory = (Join-Path $env:TEMP ('PerceptoX-validation-' + [Guid]::NewGuid().ToString('N'))),
    [switch] $OfflineConfirmed
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path.TrimEnd('\')
$result = [IO.Path]::GetFullPath($ResultDirectory)
if (Test-Path -LiteralPath $result) { throw 'Use a new result directory; existing reports are never overwritten.' }
New-Item -ItemType Directory -Path $result | Out-Null
$report = [ordered]@{
    schema = 1; utc = [DateTime]::UtcNow.ToString('o'); os = [Environment]::OSVersion.VersionString
    process64Bit = [Environment]::Is64BitProcess; powershell = $PSVersionTable.PSVersion.ToString()
    operatorConfirmedOffline = [bool]$OfflineConfirmed; cleanMachineVerified = $false
    scope = 'Package integrity, codec declarations and app-local startup; manual workflow and clean-machine certification are separate.'
    status = 'Failed'; modules = @(); error = $null
}
$process = $null
$launcher = $null
try {
    if (-not [Environment]::Is64BitProcess) { throw 'Run 64-bit Windows PowerShell.' }
    $manifest = Get-Content -LiteralPath (Join-Path $package 'package-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schema -notin @(1,2) -or $manifest.architecture -ne 'win-x64' -or -not $manifest.selfContained) { throw 'Invalid standalone manifest.' }
    $application = if ($manifest.schema -eq 2) { Join-Path $package 'app' } else { $package }
    foreach ($file in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $package $file.path))
        if (-not $path.StartsWith($package + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest escaped package.' }
        if ((Get-Item -LiteralPath $path).Length -ne $file.length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Package changed: $($file.path)" }
    }
    $codec = Join-Path $application 'codecs\imagemagick'
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $codec 'magick.exe'; $start.Arguments = '-list format'
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.EnvironmentVariables['PATH'] = $codec + ';' + [Environment]::GetFolderPath('System')
    $start.EnvironmentVariables['MAGICK_HOME'] = $codec
    $start.EnvironmentVariables['MAGICK_CONFIGURE_PATH'] = $codec
    $start.EnvironmentVariables['MAGICK_CODER_MODULE_PATH'] = Join-Path $codec 'modules\coders'
    $start.EnvironmentVariables['MAGICK_FILTER_MODULE_PATH'] = Join-Path $codec 'modules\filters'
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Codec probe timed out.' }
    $formats = $stdout.GetAwaiter().GetResult(); $errors = $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "Codec probe failed: $errors" }
    foreach ($format in @('HEIC', 'HEIF', 'AVIF')) {
        if ($formats -notmatch "(?m)^\s*$format\*?\s+\S+\s+r") { throw "Codec cannot read $format" }
    }
    $process.Dispose(); $process = $null
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = if ($manifest.schema -eq 2) { Join-Path $package 'PerceptoX.exe' } else { Join-Path $package 'PerceptoX.WinUI.exe' }; $start.WorkingDirectory = $package
    $start.UseShellExecute = $false; $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.EnvironmentVariables['PATH'] = [Environment]::GetFolderPath('System')
    $start.EnvironmentVariables.Remove('PERCEPTOX_MAGICK_PATH')
    if ($manifest.schema -eq 2) {
        $launcher = [Diagnostics.Process]::Start($start)
        $expectedExecutable = Join-Path $application 'PerceptoX.WinUI.exe'
        for ($attempt = 0; $attempt -lt 30 -and $null -eq $process; $attempt++) {
            Start-Sleep -Milliseconds 200
            foreach ($candidate in [Diagnostics.Process]::GetProcessesByName('PerceptoX.WinUI')) {
                try { if ($candidate.MainModule.FileName -eq $expectedExecutable) { $process = $candidate; break } }
                finally { if ($candidate -ne $process) { $candidate.Dispose() } }
            }
        }
        if ($null -eq $process) { throw 'Root launcher did not start its app-local child.' }
        $report['launcherStartedApp'] = $true
    } else { $process = [Diagnostics.Process]::Start($start) }
    $found = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500; $process.Refresh()
        if ($process.HasExited) { throw "Application exited during startup: $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $found = $true; break }
    }
    if (-not $found) { throw 'No application window within 15 seconds.' }
    Start-Sleep -Seconds 3; $process.Refresh()
    if ($process.HasExited) { throw 'Application exited after initialization.' }
    $loaded = @($process.Modules | Where-Object { $_.ModuleName -in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll') })
    foreach ($required in @('coreclr.dll', 'Microsoft.ui.xaml.dll')) {
        if ($required -notin $loaded.ModuleName) { throw "Runtime not loaded: $required" }
    }
    foreach ($module in $loaded) {
        if (-not $module.FileName.StartsWith($application + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "External runtime loaded: $($module.FileName)" }
        $report.modules += @{ name = $module.ModuleName; appLocal = $true }
    }
    $report.status = 'StartupPassed'
} catch {
    $report.error = $_.Exception.Message
    throw
} finally {
    if ($process) {
        if (-not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
        }
        $process.Dispose()
    }
    if ($launcher) { if (-not $launcher.WaitForExit(5000)) { $launcher.Kill(); $launcher.WaitForExit() }; $launcher.Dispose() }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $result 'startup-result.json') -Encoding utf8
    Write-Output "Validation report: $result"
}
