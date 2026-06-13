#!/usr/bin/env pwsh
<#
.SYNOPSIS
    memory-pipeline — Run the SMF Works memory continuity pipeline on Windows.

.DESCRIPTION
    Executes the core memory scripts with a working Python interpreter:
      1. memory-promotion-scanner.py --days 7 (promote !!promote blocks to MEMORY.md)
      2. (optionally) session-capture.py for a live snapshot

    Designed to run from Windows Task Scheduler every 30 minutes and at 2:15 AM ET.
    Logs output to memory/pipeline.log for troubleshooting.

.EXAMPLE
    .\memory-pipeline.ps1
    .\memory-pipeline.ps1 -Snapshot -Topic "adhoc-capture" -Decisions "ad hoc decision"
#>

param(
    [switch]$Snapshot,
    [string]$Topic = "scheduled-pipeline",
    [string]$Decisions,
    [string]$Actions
)

$ErrorActionPreference = "Stop"

$workspace = if ($env:OPENCLAW_WORKSPACE) { $env:OPENCLAW_WORKSPACE } else { "C:\Users\Michael Gannotti\.openclaw\workspace" }
$scriptsDir = Join-Path $workspace "scripts"
$memoryDir = Join-Path $workspace "memory"
$logFile = Join-Path $memoryDir "pipeline.log"

function Write-Log($Message) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message
    Add-Content -Path $logFile -Value $line -ErrorAction SilentlyContinue
    Write-Host $line
}

# Ensure log directory exists
if (-not (Test-Path $memoryDir)) { New-Item -ItemType Directory -Path $memoryDir -Force | Out-Null }

Write-Log "Memory pipeline starting in $workspace"

# Resolve Python
$findPython = Join-Path $scriptsDir "find-python.ps1"
try {
    $pythonExe = & $findPython
    Write-Log "Resolved Python: $pythonExe"
} catch {
    Write-Log "FATAL: Could not resolve Python: $_"
    exit 1
}

# 1. Promotion scan
$promotionScript = Join-Path $scriptsDir "memory-promotion-scanner.py"
try {
    Write-Log "Running promotion scanner..."
    $promoOutput = & $pythonExe $promotionScript --days 7 2>&1 | Out-String
    Write-Log "Promotion scanner output: $promoOutput"
} catch {
    Write-Log "ERROR in promotion scanner: $_"
    exit 2
}

# 2. Optional snapshot capture
if ($Snapshot) {
    $captureScript = Join-Path $scriptsDir "session-capture.py"
    $args = @($captureScript, "--topic", $Topic, "--agent", "Jeff", "--json")
    if ($Decisions) { $args += @("--decisions", $Decisions) }
    if ($Actions) { $args += @("--actions", $Actions) }
    try {
        Write-Log "Running snapshot capture..."
        $capOutput = & $pythonExe @args 2>&1 | Out-String
        Write-Log "Snapshot output: $capOutput"
    } catch {
        Write-Log "ERROR in snapshot capture: $_"
        exit 3
    }
}

Write-Log "Memory pipeline completed successfully."
exit 0
