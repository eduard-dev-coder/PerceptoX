[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($PackageDirectory)
if (Test-Path -LiteralPath (Join-Path $output 'PerceptoX.exe')) {
    & (Join-Path $PSScriptRoot 'test-offline-package.ps1') -PackageDirectory $output
    return
}
& (Join-Path $PSScriptRoot 'verify-standalone.ps1') -PackageDirectory $output
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $output 'PerceptoX.WinUI.exe'))
$start.UseShellExecute = $false
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.WorkingDirectory = $output
$start.Environment['PATH'] = [Environment]::GetFolderPath('System')
$start.Environment.Remove('PERCEPTOX_MAGICK_PATH') | Out-Null
$process = [Diagnostics.Process]::Start($start)
try {
    $found = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "Published app exited during startup: $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne 0) { $found = $true; break }
    }
    if (-not $found) { throw 'Published app did not create a window within 10 seconds.' }
    # Activation may precede asynchronous codec validation; give initialization time to complete.
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) { throw "Published app exited after initialization: $($process.ExitCode)" }
    $modules = @($process.Modules | Where-Object ModuleName -In @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll'))
    foreach ($required in @('coreclr.dll', 'Microsoft.ui.xaml.dll')) {
        if ($required -notin $modules.ModuleName) { throw "Expected runtime not loaded: $required" }
    }
    foreach ($module in $modules) {
        if (-not $module.FileName.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Machine runtime loaded: $($module.FileName)" }
    }
    [ordered]@{ applicationWindowCreated = $true; restrictedPath = $true; modules = @($modules | ForEach-Object {
        [ordered]@{ name = $_.ModuleName; path = [IO.Path]::GetRelativePath($output, $_.FileName) }
    }); scope = 'Developer-machine startup only; not a clean-Windows certification' } | ConvertTo-Json -Depth 6
} finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { $process.Kill($true); $process.WaitForExit() }
    }
    $process.Dispose()
}
