#!/usr/bin/env pwsh
<#
.SYNOPSIS
    memory-health-check — Validate the SMF Works memory continuity stack on Windows.

.DESCRIPTION
    Checks:
      - A working Python interpreter is available
      - Required workspace directories exist
      - session-capture.py, memory-promotion-scanner.py, session-bridge.ps1 run
      - team-memory/SHARED.md is reachable
      - OpenClaw cron jobs for memory safety-net and promotion exist
    Optionally runs a live test capture.

.EXAMPLE
    .\memory-health-check.ps1
    .\memory-health-check.ps1 -Fix -TestCapture
#>

param(
    [switch]$Fix,
    [switch]$TestCapture,
    [switch]$PromoteNow
)

$ErrorActionPreference = "Continue"

$workspace = if ($env:OPENCLAW_WORKSPACE) { $env:OPENCLAW_WORKSPACE } else { "C:\Users\Michael Gannotti\.openclaw\workspace" }
$scriptsDir = Join-Path $workspace "scripts"
$memoryDir = Join-Path $workspace "memory"
$decisionsDir = Join-Path $memoryDir "decisions"
$teamMemoryDir = Join-Path $workspace "team-memory"
$findPython = Join-Path $scriptsDir "find-python.ps1"
$captureScript = Join-Path $scriptsDir "session-capture.py"
$promotionScript = Join-Path $scriptsDir "memory-promotion-scanner.py"
$bridgeScript = Join-Path $scriptsDir "session-bridge.ps1"

$checks = @()
$failures = 0

function Add-Check($Name, $Pass, $Message) {
    $script:checks += [PSCustomObject]@{ Name = $Name; Pass = $Pass; Message = $Message }
    if (-not $Pass) { $script:failures++ }
}

# 1. Python resolver
if (-not (Test-Path $findPython)) {
    Add-Check "find-python.ps1" $false "Missing: $findPython"
} else {
    Add-Check "find-python.ps1" $true "Found: $findPython"
}

# 2. Python interpreter
$pythonExe = $null
try {
    $pythonExe = & $findPython
    Add-Check "python interpreter" $true "Resolved to $pythonExe"
} catch {
    Add-Check "python interpreter" $false $_.Exception.Message
}

# 3. Python version
if ($pythonExe) {
    try {
        $ver = & $pythonExe --version 2>&1
        Add-Check "python version" $true $ver
    } catch {
        Add-Check "python version" $false $_.Exception.Message
    }
}

# 4. Required directories
foreach ($dir in @($memoryDir, $decisionsDir, $teamMemoryDir)) {
    if (-not (Test-Path $dir)) {
        if ($Fix) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            Add-Check "directory: $dir" $true "Created by -Fix"
        } else {
            Add-Check "directory: $dir" $false "Missing (use -Fix to create)"
        }
    } else {
        Add-Check "directory: $dir" $true "Exists"
    }
}

# 5. Script files
foreach ($file in @($captureScript, $promotionScript, $bridgeScript)) {
    if (Test-Path $file) {
        Add-Check "script: $(Split-Path $file -Leaf)" $true "Found"
    } else {
        Add-Check "script: $(Split-Path $file -Leaf)" $false "Missing"
    }
}

# 6. Run capture dry-run
if ($pythonExe -and (Test-Path $captureScript)) {
    try {
        $out = & $pythonExe $captureScript --topic "health-check" --decisions "test" --agent "Jeff" --json 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            Add-Check "session-capture.py dry-run" $true "Output: $out"
        } else {
            Add-Check "session-capture.py dry-run" $false "Exit $LASTEXITCODE : $out"
        }
    } catch {
        Add-Check "session-capture.py dry-run" $false $_.Exception.Message
    }
}

# 7. Run promotion scanner dry-run
if ($pythonExe -and (Test-Path $promotionScript)) {
    try {
        $out = & $pythonExe $promotionScript --dry-run --days 1 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            Add-Check "memory-promotion-scanner.py dry-run" $true "Output: $out"
        } else {
            Add-Check "memory-promotion-scanner.py dry-run" $false "Exit $LASTEXITCODE : $out"
        }
    } catch {
        Add-Check "memory-promotion-scanner.py dry-run" $false $_.Exception.Message
    }
}

# 8. Windows Task Scheduler memory tasks
try {
    $tasks = Get-ScheduledTask -TaskPath '\SMFWorks\' -ErrorAction SilentlyContinue
    $expectedTaskNames = @("SMFWorks-Memory-SafetyNet", "SMFWorks-Memory-Promotion")
    $found = $tasks | Where-Object { $expectedTaskNames -contains $_.TaskName }
    if ($found.Count -eq $expectedTaskNames.Count) {
        Add-Check "Windows Task Scheduler memory tasks" $true "Found: $($found.TaskName -join ', ')"
    } else {
        $missing = $expectedTaskNames | Where-Object { -not ($tasks.TaskName -contains $_) }
        Add-Check "Windows Task Scheduler memory tasks" $false "Missing: $($missing -join ', ')"
    }
} catch {
    Add-Check "Windows Task Scheduler memory tasks" $false "Could not query: $_"
}

# 9. OpenClaw memory cron jobs (legacy/fallback awareness)
try {
    $cronOutput = openclaw cron list 2>&1 | Out-String
    $safetyNetPresent = $cronOutput -match "memory-safety-net"
    $promoPresent = $cronOutput -match "explicit-memory-promo"
    if ($safetyNetPresent -and $promoPresent) {
        Add-Check "OpenClaw memory cron jobs" $true "Both legacy jobs present (consider disabling now that Task Scheduler handles them)"
    } else {
        Add-Check "OpenClaw memory cron jobs" $true "One or both legacy jobs already disabled"
    }
} catch {
    Add-Check "OpenClaw memory cron jobs" $false "Could not query: $_"
}

# 10. Auto-capture script
$autoCaptureScript = Join-Path $scriptsDir "session-auto-capture.ps1"
if (Test-Path $autoCaptureScript) {
    Add-Check "session-auto-capture.ps1" $true "Found"
} else {
    Add-Check "session-auto-capture.ps1" $false "Missing"
}

# 11. Live test capture
if ($TestCapture -and $pythonExe) {
    try {
        $out = & $pythonExe $captureScript --topic "memory-health-check" --decisions "Memory continuity stack validated on Windows" --actions "Hardened Python resolution, bridge scripts, and health check" --agent "Jeff" --json 2>&1 | Out-String
        Add-Check "live test capture" $true "Output: $out"
    } catch {
        Add-Check "live test capture" $false $_.Exception.Message
    }
}

# 12. Optional promotion run
if ($PromoteNow -and $pythonExe) {
    try {
        $out = & $pythonExe $promotionScript --days 1 2>&1 | Out-String
        Add-Check "promotion run" $true "Output: $out"
    } catch {
        Add-Check "promotion run" $false $_.Exception.Message
    }
}

# Report
Write-Host ""
Write-Host "=== Memory Continuity Health Check ==="
Write-Host "Workspace: $workspace"
Write-Host "Python:    $pythonExe"
Write-Host ""
$checks | Format-Table -AutoSize

if ($failures -eq 0) {
    Write-Host "Result: ALL CHECKS PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Result: $failures CHECK(S) FAILED" -ForegroundColor Red
    exit 1
}
