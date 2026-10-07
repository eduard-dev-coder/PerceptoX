[CmdletBinding()]
param([Parameter(Mandatory)][string] $DeliveryDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delivery = [IO.Path]::GetFullPath($DeliveryDirectory)
$allowed = Join-Path $projectRoot 'artifacts\distribution'
if (-not $delivery.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Sources must be packaged in a new distribution artifact directory.' }
$target = Join-Path $delivery 'PerceptoX-sources'
if (Test-Path -LiteralPath $target) { throw 'Do not overwrite an existing source snapshot.' }
New-Item -ItemType Directory -Path $target | Out-Null
$files = @(Get-ChildItem -LiteralPath $projectRoot -File | Where-Object { $_.Name -in @('LICENSE', 'NOTICE', 'README.md', 'README.ro.md', 'CONTRIBUTING.md', 'CHANGELOG.md', '.gitignore', 'global.json', 'Directory.Build.props', 'Directory.Packages.props') -or $_.Extension -in @('.sln', '.slnx') })
foreach ($name in @('src', 'tests', 'tools', 'benchmarks', 'eng', 'docs', 'graphics', 'languages', 'website', '.github')) {
    $directory = Join-Path $projectRoot $name
    $files += @(Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git|\.sandbox-profile|artifacts|vendor|\.packages)[\\/]|[\\/]tools[\\/](inno-setup-[^\\/]+|downloads)[\\/]' -and
        ($_.Extension -in @('.cs', '.csproj', '.cpp', '.h', '.rc', '.iss', '.isl', '.xaml', '.manifest', '.pubxml', '.ps1', '.mjs', '.md', '.txt', '.xml', '.props', '.targets', '.icc') -or ($name -eq 'languages' -and $_.Extension -eq '.json') -or
            ($name -eq 'website' -and ($_.Extension -in @('.html','.css','.js','.json','.webp') -or $_.Name -in @('icon.png','SCREENSHOTS.md'))) -or ($name -eq '.github' -and $_.Extension -in @('.yml','.yaml')) -or
            ($name -eq 'graphics' -and $_.Name -in @('icon.png', 'logo.png', 'header-illustration.png', 'sidebar-ribbons.svg', 'empty-state.svg', 'PerceptoX.ico')))
    })
}
foreach ($file in $files) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse source file rejected.' }
    $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName)
    $destination = Join-Path $target $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'source-nuget.config.xml') -Destination (Join-Path $target 'NuGet.Config')
$zip = Join-Path $delivery 'PerceptoX-sources.zip'
Compress-Archive -LiteralPath $target -DestinationPath $zip -CompressionLevel Optimal
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output "PerceptoX source snapshot: $zip. Third-party corresponding sources are a separate release gate."
