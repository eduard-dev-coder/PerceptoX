# One-time mechanical migration helper; stable keys are stored in the catalog, not regenerated at runtime.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Add-Type -Path (Join-Path $taskRoot '.packages/microsoft.codeanalysis.common/4.14.0/lib/net9.0/Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $taskRoot '.packages/microsoft.codeanalysis.csharp/4.14.0/lib/net9.0/Microsoft.CodeAnalysis.CSharp.dll')
$taskTexts = [ordered]@{}
$taskByValue = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$taskCounters = @{}
function Register-Text([string] $prefix, [string] $value) {
    if ($taskByValue.ContainsKey($value)) { return $taskByValue[$value] }
    if (-not $taskCounters.ContainsKey($prefix)) { $taskCounters[$prefix] = 0 }
    $taskCounters[$prefix]++
    $key = '{0}.Text{1:D3}' -f $prefix, $taskCounters[$prefix]
    $taskTexts[$key] = $value
    $taskByValue[$value] = $key
    return $key
}
function Is-UiText([string] $value) {
    $plain = $value -replace '\{[^}]*\}', ''
    return $plain -match '[ăâîșțĂÂÎȘȚ]' -or ($plain -match '\p{L}' -and $plain -match '\s') -or
        $plain -in @('OK','Anulare','Anulează','Module','General','Calibrare','Procesare','Timpi','Sumar','Originale','Detalii','Eroare','Instalează','Actualizează')
}
$languages = Join-Path $taskRoot 'languages'
New-Item -ItemType Directory -Path $languages -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $languages 'ro.json')) { throw 'Migration already ran. Edit stable catalog keys directly; do not regenerate.' }
$xamlFiles = @(Get-ChildItem (Join-Path $taskRoot 'src/PerceptoX.WinUI') -Recurse -Filter '*.xaml' | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]')
$attributePattern = '(?<attribute>\b(?:Text|Header|Content|PlaceholderText|AutomationProperties.Name|ToolTipService.ToolTip))="(?<value>[^"{}]*)"'
foreach ($file in $xamlFiles) {
    $source = [IO.File]::ReadAllText($file.FullName)
    $prefix = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    $converted = [regex]::Replace($source, $attributePattern, [Text.RegularExpressions.MatchEvaluator] {
        param($match)
        $value = [Net.WebUtility]::HtmlDecode($match.Groups['value'].Value)
        if ([string]::IsNullOrWhiteSpace($value) -or $value -eq 'PerceptoX' -or $value -match '^&#?') { return $match.Value }
        $key = Register-Text $prefix $value
        return $match.Groups['attribute'].Value + '="{loc:UiTextExtension Key=' + $key + '}"'
    })
    if ($converted -ne $source) {
        $converted = [regex]::Replace($converted, 'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"', '$0 xmlns:loc="using:PerceptoX.WinUI.Localization"', 1)
        [IO.File]::WriteAllText($file.FullName, $converted)
    }
}
$csharpFiles = @(foreach ($directory in @('src/PerceptoX.Presentation/ViewModels','src/PerceptoX.Presentation/Services','src/PerceptoX.WinUI')) {
    Get-ChildItem (Join-Path $taskRoot $directory) -Recurse -Filter '*.cs' | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|Localization)[\\/]' -and $_.BaseName -notin @('DesignPreviewCapture','UxValidationWorkflow','UxProfileOptions')
    }
})
foreach ($file in $csharpFiles) {
    $source = [IO.File]::ReadAllText($file.FullName)
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source)
    $changes = [Collections.Generic.List[object]]::new()
    foreach ($node in $tree.GetRoot().DescendantNodes()) {
        if ($node.Ancestors() | Where-Object { $_.GetType().Name -eq 'InterpolatedStringExpressionSyntax' -or $_.GetType().Name -eq 'AttributeSyntax' }) { continue }
        $arguments = [Collections.Generic.List[string]]::new()
        $value = $null
        if ($node.GetType().Name -eq 'LiteralExpressionSyntax' -and $node.Token.Value -is [string]) {
            $value = [string]$node.Token.Value
        } elseif ($node.GetType().Name -eq 'InterpolatedStringExpressionSyntax') {
            $builder = [Text.StringBuilder]::new()
            foreach ($part in $node.Contents) {
                if ($part.GetType().Name -eq 'InterpolatedStringTextSyntax') {
                    [void]$builder.Append($part.TextToken.ValueText.Replace('{','{{').Replace('}','}}'))
                } else {
                    $hole = '{' + $arguments.Count
                    if ($part.AlignmentClause) { $hole += ',' + $part.AlignmentClause.Value.ToString() }
                    if ($part.FormatClause) { $hole += ':' + $part.FormatClause.FormatStringToken.ValueText }
                    [void]$builder.Append($hole + '}')
                    $arguments.Add($part.Expression.ToString())
                }
            }
            $value = $builder.ToString()
        }
        if ($null -eq $value -or -not (Is-UiText $value)) { continue }
        # Pure schema keys, immutable processing identity, paths and enums stay untouched.
        $key = Register-Text $file.BaseName $value
        $replacement = 'UiText.T("' + $key + '"'
        foreach ($argument in $arguments) { $replacement += ', ' + $argument }
        $replacement += ')'
        $changes.Add(@{ Start = $node.Span.Start; Length = $node.Span.Length; Replacement = $replacement })
    }
    if ($changes.Count -gt 0) {
        foreach ($change in ($changes | Sort-Object Start -Descending)) {
            $source = $source.Remove($change.Start, $change.Length).Insert($change.Start, $change.Replacement)
        }
        $source = "using PerceptoX.Presentation.Localization;`n" + $source
        [IO.File]::WriteAllText($file.FullName, $source)
    }
}
$taskTexts['Language.Restart'] = 'Limba aleasă: {0}. Repornește PerceptoX pentru a o aplica.'
$taskTexts['Language.SaveFailure'] = 'Limba nu a putut fi salvată: {0}'
$taskTexts['Language.InvalidPack'] = 'Un catalog de limbă nu este valid și a fost ignorat. Româna și engleza rămân disponibile.'
$taskTexts['Language.PreferenceFailure'] = 'Preferința de limbă nu a putut fi citită. S-a folosit româna.'
foreach ($code in @('ro','en')) {
    $pack = [ordered]@{ schemaVersion = 1; language = $code; displayName = $(if ($code -eq 'ro') { 'Română' } else { 'English' }); strings = $taskTexts }
    [IO.File]::WriteAllText((Join-Path $languages ($code + '.json')), ($pack | ConvertTo-Json -Depth 5))
}
Write-Output ('Extracted {0} UI entries. English needs translation; do not release the initial mirror.' -f $taskTexts.Count)
