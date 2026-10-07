[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$taskTheme = Get-Content -LiteralPath (Join-Path $taskRoot 'graphics/PerceptoX.Theme.xaml') -Raw
$taskNs = New-Object Xml.XmlNamespaceManager($taskTheme.NameTable)
$taskNs.AddNamespace('p', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$taskNs.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$taskPalettes = @{}
foreach ($taskMode in @('Light', 'Dark')) {
    $taskNode = $taskTheme.SelectSingleNode("//p:ResourceDictionary[@x:Key='$taskMode']", $taskNs)
    if ($null -eq $taskNode) { throw "Missing palette $taskMode" }
    $taskMap = @{}
    foreach ($taskBrush in $taskNode.ChildNodes) {
        $taskKey = $taskBrush.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')
        if ($taskMap.ContainsKey($taskKey)) { throw "Duplicate $taskMode resource $taskKey" }
        $taskMap[$taskKey] = $taskBrush
    }
    $taskPalettes[$taskMode] = $taskMap
}
if (@(Compare-Object @($taskPalettes.Light.Keys | Sort-Object) @($taskPalettes.Dark.Keys | Sort-Object)).Count) { throw 'Palette keys differ.' }
function Get-Luminance([string] $Color) {
    if ($Color -eq 'White') { $Color = '#FFFFFF' }
    $taskHex = $Color.TrimStart('#')
    if ($taskHex.Length -eq 8) { $taskHex = $taskHex.Substring(2) }
    $taskChannels = foreach ($taskIndex in @(0,2,4)) {
        $taskComponent = [Convert]::ToInt32($taskHex.Substring($taskIndex,2),16) / 255.0
        if ($taskComponent -le 0.04045) { $taskComponent / 12.92 } else { [Math]::Pow(($taskComponent + 0.055) / 1.055, 2.4) }
    }
    return 0.2126*$taskChannels[0] + 0.7152*$taskChannels[1] + 0.0722*$taskChannels[2]
}
foreach ($taskMode in @('Light','Dark')) {
    $taskPalette = $taskPalettes[$taskMode]
    foreach ($taskPair in @(@('PxInk','PxCard'), @('PxMuted','PxCard'), @('PxAccentText','PxCard'), @('PxSuccessInk','PxSuccessSurface'), @('PxWarningInk','PxWarningSurface'), @('AccentButtonForeground','AccentButtonBackground'))) {
        $taskA = Get-Luminance $taskPalette[$taskPair[0]].Color
        $taskB = Get-Luminance $taskPalette[$taskPair[1]].Color
        $taskRatio = ([Math]::Max($taskA,$taskB)+0.05)/([Math]::Min($taskA,$taskB)+0.05)
        if ($taskRatio -lt 4.5) { throw "$taskMode $($taskPair -join '/') contrast $taskRatio is below 4.5:1" }
        Write-Output ("{0} {1}: {2:F2}:1" -f $taskMode,($taskPair -join '/'),$taskRatio)
    }
}
foreach ($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src/PerceptoX.WinUI') -Filter *.xaml -Recurse | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }) {
    $taskText = [IO.File]::ReadAllText($taskFile.FullName)
    if ($taskText -match '\{StaticResource Px(?:Ink|Muted|Blue|Line|Card|Background)\}' -or $taskText -match 'RequestedTheme="Light"') { throw "Fixed palette in $($taskFile.Name)" }
}
Write-Output 'Light/Dark palette parity and selected normal-text contrasts verified. Not a full accessibility certification.'
