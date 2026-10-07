[CmdletBinding(DefaultParameterSetName = 'InlinePrompt')]
param(
    [Parameter(Mandatory)]
    [ValidateSet(
        'qwen2.5-coder:14b',
        'qwen2.5-coder:32b',
        'deepseek-coder-v2:latest',
        'gemma4:31b',
        'gemma4:latest')]
    [string] $Model,

    [Parameter(Mandatory, ParameterSetName = 'InlinePrompt')]
    [ValidateNotNullOrEmpty()]
    [string] $Prompt,

    [Parameter(Mandatory, ParameterSetName = 'PromptFile')]
    [ValidateNotNullOrEmpty()]
    [string] $PromptFile,

    [ValidateNotNullOrEmpty()]
    [uri] $BaseUri = 'http://127.0.0.1:11434',

    [string] $ApiKey = $env:PERCEPTOX_LOCAL_AI_API_KEY,

    [ValidateRange(128, 4096)]
    [int] $MaxTokens = 1200,

    [ValidateRange(0.0, 1.0)]
    [double] $Temperature = 0.1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSCmdlet.ParameterSetName -eq 'PromptFile') {
    $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $resolvedPromptFile = (Resolve-Path -LiteralPath $PromptFile).Path
    $promptPath = [System.IO.Path]::GetFullPath($resolvedPromptFile)
    if (-not $promptPath.StartsWith(
            $repositoryRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Prompt files must be located inside the PerceptoX workspace.'
    }
    $promptText = Get-Content -LiteralPath $resolvedPromptFile -Raw
}
else {
    $promptText = $Prompt
}

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $headers['Authorization'] = "Bearer $ApiKey"
}

$base = $BaseUri.AbsoluteUri.TrimEnd('/')
if (-not $BaseUri.IsLoopback -or $BaseUri.Scheme -notin @('http', 'https') -or $BaseUri.UserInfo) {
    throw 'Local delegation only: use a loopback Ollama endpoint without URI credentials.'
}

try {
    $catalog = Invoke-RestMethod `
        -Method Get `
        -Uri "$base/api/tags" `
        -Headers $headers `
        -TimeoutSec 15
}
catch {
    $statusCode = $null
    if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode
    }

    if ($statusCode -eq 401) {
        throw "Local AI endpoint '$base' cere autorizare. Setați PERCEPTOX_LOCAL_AI_API_KEY sau transmiteți -ApiKey, apoi reluați delegarea."
    }

    throw "Nu pot citi catalogul de modele de la '$base/api/tags': $($_.Exception.Message)"
}

$installedModels = @($catalog.models | ForEach-Object { $_.name })
if ($installedModels -notcontains $Model) {
    $installedLabel = if ($installedModels.Count -eq 0) {
        '(niciun model instalat)'
    }
    else {
        $installedModels -join ', '
    }

    throw "Modelul local '$Model' nu este disponibil la '$base'. Modele disponibile: $installedLabel. " +
        "Pentru Ollama clasic instalați-l explicit cu 'ollama pull $Model' înainte de delegare."
}
$selected = @($catalog.models | Where-Object name -EQ $Model)[0]
if ($selected.PSObject.Properties.Name -contains 'remote_model' -and $selected.remote_model) {
    throw "Model '$Model' uses a remote provider; local delegation must not send project content to cloud models."
}

$request = @{
    model = $Model
    prompt = $promptText
    stream = $false
    think = $false
    keep_alive = '5m'
    options = @{
        temperature = $Temperature
        num_predict = $MaxTokens
        num_ctx = 4096
    }
} | ConvertTo-Json -Depth 5

$response = Invoke-RestMethod `
    -Method Post `
    -Uri "$base/api/generate" `
    -Headers $headers `
    -ContentType 'application/json' `
    -Body $request `
    -TimeoutSec 600

if (-not $response.done) {
    throw "Ollama did not complete the request for model '$Model'."
}

$response.response
