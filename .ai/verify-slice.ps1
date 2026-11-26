<#
.SYNOPSIS
    Runs the Stage 5 deterministic build & test gate and captures concise errors for Ollama Fix.

.DESCRIPTION
    Executes 'dotnet build -warnaserror' and 'dotnet test' (plus optional 'dotnet format --verify-no-changes').
    On failure, strips verbose MSBuild noise and writes concise error lines to .ai\last-error.txt
    so '.ai\run-stage.ps1 -Stage Fix' stays strictly within the 8192-token local context ceiling.

.EXAMPLE
    .\.ai\verify-slice.ps1

.EXAMPLE
    .\.ai\verify-slice.ps1 -FormatCheck
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Target = '',

    [Parameter(Mandatory = $false)]
    [string]$ErrorLogPath = '.ai\last-error.txt',

    [Parameter(Mandatory = $false)]
    [int]$MaxErrorLines = 120,

    [Parameter(Mandatory = $false)]
    [switch]$FormatCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

function Write-ConciseErrorLog {
    param(
        [string]$Phase,
        [string[]]$RawOutput
    )

    $filtered = @($RawOutput | Where-Object {
        $_ -match '(?i)(error\s+[A-Z]+\d+|warning\s+[A-Z]+\d+|Failed\s+|Error Message:|Stack Trace:|Expected\s+|Assert\.|Unhandled exception)'
    })

    if ($filtered.Count -eq 0) {
        $filtered = @($RawOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 60)
    }

    $clipped = @($filtered | Select-Object -First $MaxErrorLines)

    $outDir = Split-Path -Path $ErrorLogPath -Parent
    if ($outDir -and -not (Test-Path -Path $outDir -PathType Container)) {
        New-Item -Path $outDir -ItemType Directory -Force | Out-Null
    }

    $header = @(
        "=== Stage 5 Failure ($Phase) ===",
        "Captured: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        ""
    )
    Set-Content -Path $ErrorLogPath -Value ($header + $clipped) -Encoding UTF8

    Write-Host ""
    Write-Host "FAILED ($Phase). Concise error log saved to: $ErrorLogPath ($($clipped.Count) lines)" -ForegroundColor Red
    Write-Host "Next step (max 2 local retries):" -ForegroundColor Yellow
    Write-Host "  .\.ai\run-stage.ps1 -Stage Fix -SlicePath .ai\slices\slice-XX.md -TargetFiles <files> -ErrorLog $ErrorLogPath" -ForegroundColor Yellow
}

$targetArgs = if ([string]::IsNullOrWhiteSpace($Target)) { @() } else { @($Target) }

# 1. Optional format check
if ($FormatCheck) {
    Write-Host "[1/3] Running dotnet format --verify-no-changes..." -ForegroundColor Cyan
    $formatOut = & dotnet format @targetArgs --verify-no-changes --no-restore 2>&1 | ForEach-Object { $_.ToString() }
    if ($LASTEXITCODE -ne 0) {
        Write-ConciseErrorLog -Phase 'dotnet format --verify-no-changes' -RawOutput $formatOut
        exit $LASTEXITCODE
    }
}

# 2. Deterministic build with warnings as errors
Write-Host "Running dotnet build -warnaserror..." -ForegroundColor Cyan
$buildOut = & dotnet build @targetArgs -warnaserror --nologo -v minimal 2>&1 | ForEach-Object { $_.ToString() }
if ($LASTEXITCODE -ne 0) {
    Write-ConciseErrorLog -Phase 'dotnet build -warnaserror' -RawOutput $buildOut
    exit $LASTEXITCODE
}

# 3. Run automated tests
Write-Host "Running dotnet test..." -ForegroundColor Cyan
$testOut = & dotnet test @targetArgs --no-build --nologo --verbosity minimal 2>&1 | ForEach-Object { $_.ToString() }
if ($LASTEXITCODE -ne 0) {
    Write-ConciseErrorLog -Phase 'dotnet test' -RawOutput $testOut
    exit $LASTEXITCODE
}

# Clean up stale error log on green pass
if (Test-Path -Path $ErrorLogPath -PathType Leaf) {
    Remove-Item -Path $ErrorLogPath -Force
}

Write-Host ""
Write-Host "PASSED: dotnet build -warnaserror and dotnet test are 100% green." -ForegroundColor Green
exit 0

