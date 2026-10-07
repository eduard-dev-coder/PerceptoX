# Local-only auxiliary translation; output must be reviewed before release.
[CmdletBinding()]
param([string] $Model = 'gemma4:latest')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ro = Get-Content (Join-Path $taskRoot 'languages/ro.json') -Raw | ConvertFrom-Json -AsHashtable
$enPath = Join-Path $taskRoot 'languages/en.json'
$en = Get-Content $enPath -Raw | ConvertFrom-Json -AsHashtable
$catalog = Invoke-RestMethod http://127.0.0.1:11434/api/tags -TimeoutSec 15
$selected = @($catalog.models | Where-Object name -EQ $Model)
if ($selected.Count -ne 1 -or $Model -match ':cloud$' -or ($selected[0].PSObject.Properties['remote_model'] -and $selected[0].remote_model)) {
    throw 'A genuinely local installed Ollama model is required.'
}
$keys = @($ro.strings.Keys | Where-Object { $en.strings[$_] -eq $ro.strings[$_] })
for ($offset = 0; $offset -lt $keys.Count; $offset += 16) {
    $batch = [ordered]@{}
    foreach ($key in $keys[$offset..([Math]::Min($offset + 15, $keys.Count - 1))]) { $batch[$key] = $ro.strings[$key] }
    $prompt = 'Translate Romanian desktop UI to clear concise English. Return only a JSON object with the exact same keys and translated string values. Preserve .NET format placeholders EXACTLY, including {0:F2}, {0:hh\\:mm\\:ss}; keep their count, punctuation, newlines and leading/trailing spaces. Do not translate brand names, algorithms, filenames, license IDs or extensions. Preserve all consent and safety warnings. Input: ' + ($batch | ConvertTo-Json -Compress)
    $request = @{ model = $Model; prompt = $prompt; format = 'json'; stream = $false; think = $false;
        options = @{ temperature = 0; num_ctx = 4096; num_predict = 3000 } } | ConvertTo-Json -Depth 6
    $valid = $false
    for ($attempt = 0; $attempt -lt 2 -and -not $valid; $attempt++) {
        $response = Invoke-RestMethod http://127.0.0.1:11434/api/generate -Method Post -Body $request -ContentType 'application/json' -TimeoutSec 180
        try {
            $translations = $response.response | ConvertFrom-Json -AsHashtable
            foreach ($key in $batch.Keys) {
                if (-not $translations.ContainsKey($key) -or $translations[$key] -isnot [string]) { throw 'Missing translation.' }
                $before = @([regex]::Matches($batch[$key], '\{\d+(?:,[^{}]+)?(?::[^{}]+)?\}') | ForEach-Object Value | Sort-Object)
                $after = @([regex]::Matches($translations[$key], '\{\d+(?:,[^{}]+)?(?::[^{}]+)?\}') | ForEach-Object Value | Sort-Object)
                if (($before -join '|') -ne ($after -join '|')) { throw "Changed placeholders: $key" }
                [void][Text.CompositeFormat]::Parse($translations[$key])
            }
            $valid = $true
        } catch { Write-Warning ('Retrying invalid translation batch: ' + $_.Exception.Message) }
    }
    if (-not $valid) { throw 'Translation failed validation. Completed batches retained for review.' }
    foreach ($key in $batch.Keys) { $en.strings[$key] = $translations[$key] }
    [IO.File]::WriteAllText($enPath, ($en | ConvertTo-Json -Depth 5))
    Write-Output ('Translated batch {0}/{1}' -f (1 + [int]($offset / 16)), [Math]::Ceiling($keys.Count / 16))
}
