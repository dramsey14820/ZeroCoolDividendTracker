<#
.SYNOPSIS
    Generates a compact, signature-only .ai\repo-map.md for low-token Stage 2 planning.

.DESCRIPTION
    Scans .cs, .razor, and .sql files (excluding bin, obj, .git, .vs, .ai, .agents, and generated files)
    and extracts only namespaces, type declarations, Razor @page/@inject directives, and SQL object
    headers (no method bodies). Feed .ai\repo-map.md to Gemini in Stage 2 instead of full source files.

.EXAMPLE
    .\.ai\update-repo-map.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$OutputPath = '.ai\repo-map.md'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Get-Location).Path
$excludedDirs = @('bin', 'obj', '.git', '.vs', '.ai', '.agents', 'node_modules', 'packages')

function Test-ExcludedPath {
    param([string]$RelativePath)
    $parts = $RelativePath -split '[\\/]'
    foreach ($p in $parts) {
        if ($excludedDirs -contains $p) { return $true }
    }
    return $false
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Repository Map (Auto-Generated)')
$lines.Add('')
$lines.Add('> Compact signature-only index for Stage 2 slice planning. Regenerate after commits via `.\.ai\update-repo-map.ps1`.')
$lines.Add('')

$allFiles = Get-ChildItem -Path $repoRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        $rel = $_.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
        -not (Test-ExcludedPath -RelativePath $rel) -and
        $_.Name -notmatch '\.(g|Designer|AssemblyInfo)\.cs$' -and
        $_.Name -notmatch 'ModelSnapshot\.cs$'
    } |
    Sort-Object FullName

# 1. C# Types & Contracts
$csFiles = @($allFiles | Where-Object { $_.Extension -eq '.cs' })
$lines.Add('## C# Types & Interfaces')
if ($csFiles.Count -eq 0) {
    $lines.Add('- *(none yet)*')
} else {
    foreach ($file in $csFiles) {
        $rel = ($file.FullName.Substring($repoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
        $content = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
        $sigs = [System.Collections.Generic.List[string]]::new()

        foreach ($line in $content) {
            $trimmed = $line.Trim()
            if ($trimmed -match '^(public|internal)\s+(sealed\s+|static\s+|abstract\s+|partial\s+)*(interface|record|class|enum|struct)\s+\S+') {
                $sigs.Add(($trimmed -replace '\s*\{.*$', ''))
            }
        }

        if ($sigs.Count -gt 0) {
            $lines.Add("- ``$rel``: " + ($sigs -join '; '))
        }
    }
}
$lines.Add('')

# 2. Razor Components & Routes
$razorFiles = @($allFiles | Where-Object { $_.Extension -eq '.razor' })
$lines.Add('## Razor Components')
if ($razorFiles.Count -eq 0) {
    $lines.Add('- *(none yet)*')
} else {
    foreach ($file in $razorFiles) {
        $rel = ($file.FullName.Substring($repoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
        $content = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
        $meta = [System.Collections.Generic.List[string]]::new()

        foreach ($line in $content) {
            $trimmed = $line.Trim()
            if ($trimmed -match '^@(page|inject)\s+') {
                $meta.Add($trimmed)
            }
        }

        if ($meta.Count -gt 0) {
            $lines.Add("- ``$rel``: " + ($meta -join ' | '))
        } else {
            $lines.Add("- ``$rel``")
        }
    }
}
$lines.Add('')

# 3. SQL Schema Objects
$sqlFiles = @($allFiles | Where-Object { $_.Extension -eq '.sql' })
$lines.Add('## SQL Schema')
if ($sqlFiles.Count -eq 0) {
    $lines.Add('- *(none yet)*')
} else {
    foreach ($file in $sqlFiles) {
        $rel = ($file.FullName.Substring($repoRoot.Length).TrimStart('\', '/')) -replace '\\', '/'
        $content = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
        $objs = [System.Collections.Generic.List[string]]::new()

        foreach ($line in $content) {
            $trimmed = $line.Trim()
            if ($trimmed -match '^(?i)CREATE\s+(TABLE|VIEW|PROCEDURE|PROC|INDEX)\s+\S+') {
                $objs.Add(($trimmed -replace '\s*\(.*$', ''))
            }
        }

        if ($objs.Count -gt 0) {
            $lines.Add("- ``$rel``: " + ($objs -join '; '))
        } else {
            $lines.Add("- ``$rel``")
        }
    }
}

$outDir = Split-Path -Path $OutputPath -Parent
if ($outDir -and -not (Test-Path -Path $outDir -PathType Container)) {
    New-Item -Path $outDir -ItemType Directory -Force | Out-Null
}

$outputText = ($lines -join "`n") + "`n"
Set-Content -Path $OutputPath -Value $outputText -Encoding UTF8

$estTokens = [math]::Ceiling($outputText.Length / 3.8)
Write-Host "Updated $OutputPath (~$estTokens tokens; $($csFiles.Count) .cs, $($razorFiles.Count) .razor, $($sqlFiles.Count) .sql scanned)." -ForegroundColor Green

