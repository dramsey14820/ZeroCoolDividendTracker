<#
.SYNOPSIS
    Runs a local Ollama stage (Tests, Implement, Fix, or Review) for the ZeroCool AI Coding Pipeline.

.DESCRIPTION
    Enforces the RTX 4060 (8 GB VRAM) 8192-token context ceiling (num_ctx: 8192) and automatically
    routes to the configured local model:
      - Tests / Implement -> qwen2.5-coder:7b
      - Fix / Review      -> qwen3.5:9b

    Packs ONLY .ai\constitution.md, the active slice-XX.md, optional UI rules, specified target
    files, and (for Fix/Review) error logs or git diff --staged. Never dumps the full repository.

.EXAMPLE
    .\.ai\run-stage.ps1 -Stage Tests -SlicePath .ai\slices\slice-01.md

.EXAMPLE
    .\.ai\run-stage.ps1 -Stage Implement -SlicePath .ai\slices\slice-01.md -TargetFiles tests\MyApp.Tests\SmokeTests.cs -IncludeUiRules

.EXAMPLE
    .\.ai\run-stage.ps1 -Stage Fix -SlicePath .ai\slices\slice-01.md -TargetFiles src\MyApp\Program.cs -ErrorLog .ai\last-error.txt

.EXAMPLE
    .\.ai\run-stage.ps1 -Stage Review -SlicePath .ai\slices\slice-01.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Tests', 'Implement', 'Fix', 'Review')]
    [string]$Stage,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SlicePath,

    [Parameter(Mandatory = $false)]
    [string[]]$TargetFiles = @(),

    [Parameter(Mandatory = $false)]
    [string]$ErrorLog,

    [Parameter(Mandatory = $false)]
    [switch]$IncludeUiRules,

    [Parameter(Mandatory = $false)]
    [switch]$IncludeGoldenExample,

    [Parameter(Mandatory = $false)]
    [string]$OllamaUrl = 'http://localhost:11434',

    [Parameter(Mandatory = $false)]
    [string]$OutputPath,

    [Parameter(Mandatory = $false)]
    [switch]$FullDiff,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$maxNumCtx = 8192
$warnInputTokens = 6500

# Model routing per ZeroCool AGENTS.md
$model = switch ($Stage) {
    'Tests'     { 'qwen2.5-coder:7b' }
    'Implement' { 'qwen2.5-coder:7b' }
    'Fix'       { 'qwen3.5:9b' }
    'Review'    { 'qwen3.5:9b' }
}

# Validate required files
$constitutionPath = '.ai\constitution.md'
if (-not (Test-Path -Path $constitutionPath -PathType Leaf)) {
    throw "Missing '$constitutionPath'. Run this script from the repository root."
}
if (-not (Test-Path -Path $SlicePath -PathType Leaf)) {
    throw "Slice file not found: '$SlicePath'."
}

# Assemble minimal context payload
$sections = [System.Collections.Generic.List[string]]::new()

$sections.Add("## Project Constitution (.ai/constitution.md)`n" + (Get-Content -Path $constitutionPath -Raw))

if ($IncludeUiRules) {
    $uiRulesPath = '.agents\rules\blazor-maui-ui.md'
    if (Test-Path -Path $uiRulesPath -PathType Leaf) {
        $sections.Add("## Blazor & MAUI UI Rules (.agents/rules/blazor-maui-ui.md)`n" + (Get-Content -Path $uiRulesPath -Raw))
    } else {
        Write-Warning "UI rules file not found at '$uiRulesPath'; continuing without it."
    }
}

if ($IncludeGoldenExample) {
    $goldenPath = '.ai\golden-slice-example.md'
    if (Test-Path -Path $goldenPath -PathType Leaf) {
        $sections.Add("## Golden Slice Reference (.ai/golden-slice-example.md)`n" + (Get-Content -Path $goldenPath -Raw))
    } else {
        Write-Warning "Golden slice example not found at '$goldenPath'; continuing without it."
    }
}

$sections.Add("## Active Vertical Slice ($SlicePath)`n" + (Get-Content -Path $SlicePath -Raw))

foreach ($file in $TargetFiles) {
    if (-not (Test-Path -Path $file -PathType Leaf)) {
        throw "Target file not found: '$file'."
    }
    $sections.Add("## File: $file`n``````csharp`n" + (Get-Content -Path $file -Raw) + "`n``````")
}

if ($Stage -eq 'Fix') {
    if ([string]::IsNullOrWhiteSpace($ErrorLog) -or -not (Test-Path -Path $ErrorLog -PathType Leaf)) {
        throw "Stage 'Fix' requires -ErrorLog pointing to a file containing the concise compiler/test errors."
    }
    $sections.Add("## Failing Build / Test Output ($ErrorLog)`n``````text`n" + (Get-Content -Path $ErrorLog -Raw) + "`n``````")
}

if ($Stage -eq 'Review') {
    if ($FullDiff) {
        $stagedDiff = (git diff --staged) -join "`n"
    } else {
        $stagedDiff = (git diff --staged -U2 -- . ':(exclude)*.Designer.cs' ':(exclude)*ModelSnapshot.cs' ':(exclude)*.g.cs' ':(exclude)*.svg' ':(exclude)*.lock') -join "`n"
    }
    if ([string]::IsNullOrWhiteSpace($stagedDiff)) {
        throw "No staged changes found. Stage your slice files with 'git add' before running Stage 'Review'."
    }
    $sections.Add("## Staged Git Diff (git diff --staged)`n``````diff`n$stagedDiff`n``````")
}

# Stage-specific system & user instructions
$systemPrompt = @"
You are a senior .NET 10+ LTS C# engineer operating inside the ZeroCool AI Coding Pipeline.
Strictly follow the Project Constitution:
- Explicit public class Program with static void Main(string[] args). NEVER use C# top-level statements.
- Nullable reference types enabled, zero warnings (-warnaserror), file-scoped namespaces.
- Vertical-slice architecture; Razor components inject I*Service only; pure CSS Grid & Flexbox with .razor.css isolation.
- Tests use xUnit + FluentAssertions + NSubstitute (+ bUnit for Razor).
Keep responses focused strictly on the active slice.
"@

$stageTask = switch ($Stage) {
    'Tests' {
        "Stage 3 (Red Phase): Generate complete, compilable xUnit (+ FluentAssertions + NSubstitute, and bUnit for Razor) test file(s) covering EVERY Gherkin Scenario in the active slice. Output each file clearly labeled with its relative path."
    }
    'Implement' {
        "Stage 4 (Green Phase): Implement the production code for the target files listed in the active slice so all Gherkin scenarios and tests pass. Output each file clearly labeled with its relative path."
    }
    'Fix' {
        "Stage 5 (Local Repair): Analyze the failing build/test output and provide minimal, targeted code fixes for the affected file(s) so 'dotnet build -warnaserror' and 'dotnet test' pass cleanly."
    }
    'Review' {
        "Stage 6 (Pass-1 Local Diff Review): Audit the staged git diff against the Project Constitution and the active slice's Gherkin scenarios. List any defects, missed edge cases, C#/Blazor/MAUI/WiX rule violations, or security risks. If clean, state PASS."
    }
}

$userPrompt = ($sections -join "`n`n---`n`n") + "`n`n---`n`n## Task`n$stageTask"

# Pre-flight token estimate (~3.8 chars/token conservative heuristic for code + markdown)
$totalChars = $systemPrompt.Length + $userPrompt.Length
$estInputTokens = [math]::Ceiling($totalChars / 3.8)

Write-Host "ZeroCool Local Ollama Runner" -ForegroundColor Cyan
Write-Host "  Stage            : $Stage"
Write-Host "  Model            : $model (num_ctx: $maxNumCtx)"
Write-Host "  Endpoint         : $OllamaUrl"
Write-Host "  Estimated Input  : ~$estInputTokens tokens ($totalChars chars)"

if ($estInputTokens -gt $warnInputTokens) {
    Write-Warning "Estimated input (~$estInputTokens tokens) is close to or exceeds the $maxNumCtx token ceiling! Reduce -TargetFiles or trim the error log to avoid truncation."
}

if ($DryRun) {
    Write-Host "`n[DryRun] Request not sent to Ollama." -ForegroundColor Yellow
    return
}

$payload = @{
    model    = $model
    stream   = $false
    options  = @{
        num_ctx     = $maxNumCtx
        temperature = 0.2
    }
    messages = @(
        @{ role = 'system'; content = $systemPrompt },
        @{ role = 'user';   content = $userPrompt }
    )
} | ConvertTo-Json -Depth 6

Write-Host "`nSending request to Ollama..." -ForegroundColor Yellow

try {
    $utf8Body = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $response = Invoke-RestMethod -Uri "$OllamaUrl/api/chat" -Method Post -ContentType 'application/json; charset=utf-8' -Body $utf8Body
} catch {
    throw "Failed to reach Ollama at '$OllamaUrl/api/chat'. Ensure Ollama is running ('ollama serve') and '$model' is installed. Details: $($_.Exception.Message)"
}

$content = $response.message.content

if ($OutputPath) {
    $outDir = Split-Path -Path $OutputPath -Parent
    if ($outDir -and -not (Test-Path -Path $outDir -PathType Container)) {
        New-Item -Path $outDir -ItemType Directory -Force | Out-Null
    }
    Set-Content -Path $OutputPath -Value $content -Encoding UTF8
    Write-Host "Saved output to: $OutputPath" -ForegroundColor Green
} else {
    Write-Host "`n================ OLLAMA RESPONSE ================`n" -ForegroundColor Green
    Write-Output $content
    Write-Host "`n=================================================" -ForegroundColor Green
}

if ($response.PSObject.Properties['prompt_eval_count'] -and $response.PSObject.Properties['eval_count']) {
    Write-Host ("Actual Tokens    : prompt={0}, output={1}, total={2} / {3}" -f `
        $response.prompt_eval_count, `
        $response.eval_count, `
        ($response.prompt_eval_count + $response.eval_count), `
        $maxNumCtx) -ForegroundColor DarkGray
}

