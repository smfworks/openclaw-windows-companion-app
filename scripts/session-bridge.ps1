#!/usr/bin/env pwsh
<#
.SYNOPSIS
    session-bridge — Conversational memory capture CLI for SMF Works agents.
    
.DESCRIPTION
    Wraps session-capture.py for easy conversational use. Supports:
    - capture: Full structured capture with key-value fields
    - decide: Quick single-decision capture
    - snapshot: Full conversation snapshot
    - list: List decisions for a topic
    - search: Search decision files for a query

.PARAMETER Command
    One of: capture, decide, snapshot, list, search

.EXAMPLE
    session-bridge capture --topic "aionas-eyes" --decisions "React Native + Expo" --actions "Draft spec"
    
.EXAMPLE
    session-bridge decide --topic "model-selection" --decision "deepseek-v4-pro for complex tasks"
    
.EXAMPLE
    session-bridge list --topic "postiz"
    
.EXAMPLE
    session-bridge search --query "webrtc"

.NOTES
    Author: Aiona Edge, SMF Works Project (2026-05-26)
    Adapted for Windows/PowerShell: Jeff (2026-05-27)
#>

param(
    [Parameter(Position=0, Mandatory=$true)]
    [ValidateSet("capture", "decide", "snapshot", "list", "search")]
    [string]$Command,
    
    [Parameter()]
    [string]$Topic,
    
    [Parameter()]
    [string]$Decisions,
    
    [Parameter()]
    [string]$Architectures,
    
    [Parameter()]
    [string]$Actions,
    
    [Parameter()]
    [string]$Dependencies,
    
    [Parameter()]
    [string]$OpenThreads,
    
    [Parameter()]
    [string]$Agent = "Jeff",
    
    [Parameter()]
    [string]$Decision,
    
    [Parameter()]
    [string]$Rationale,
    
    [Parameter()]
    [string]$Query
)

$workspace = if ($env:OPENCLAW_WORKSPACE) { 
    $env:OPENCLAW_WORKSPACE 
} else { 
    "C:\Users\Michael Gannotti\.openclaw\workspace" 
}

$captureScript = Join-Path $workspace "scripts\session-capture.py"
$decisionsDir = Join-Path $workspace "memory\decisions"

# Resolve a real Python interpreter on Windows (avoids broken python3 Store alias)
$findPython = Join-Path $workspace "scripts\find-python.ps1"
$pythonExe = if (Test-Path $findPython) {
    & $findPython
} else {
    "python"
}

if (-not $pythonExe -or -not (Test-Path $pythonExe)) {
    # Fallback to PATH-based python if resolver fails
    $cmd = Get-Command "python" -ErrorAction SilentlyContinue
    if ($cmd) { $pythonExe = $cmd.Source }
}

if (-not $pythonExe) {
    throw "No usable Python interpreter found. Run scripts/find-python.ps1 to diagnose."
}

function Invoke-Capture {
    $args = @($captureScript, "--topic", $Topic, "--agent", $Agent, "--json")
    
    if ($Decisions) { $args += "--decisions"; $args += $Decisions }
    if ($Architectures) { $args += "--architectures"; $args += $Architectures }
    if ($Actions) { $args += "--actions"; $args += $Actions }
    if ($Dependencies) { $args += "--dependencies"; $args += $Dependencies }
    if ($OpenThreads) { $args += "--open-threads"; $args += $OpenThreads }
    
    & $pythonExe $args
}

function Invoke-Decide {
    if (-not $Topic) { Write-Error "decide requires --topic"; return }
    if (-not $Decision) { Write-Error "decide requires --decision"; return }
    
    $rationaleArg = if ($Rationale) { $Rationale } else { "Captured via session-bridge decide" }
    
    & $pythonExe $captureScript --topic $Topic --decisions $Decision --architectures $rationaleArg --agent $Agent --json
}

function Invoke-List {
    if (-not $Topic) { Write-Error "list requires --topic"; return }
    
    $safeTopic = $Topic.ToLower().Replace(" ", "-").Replace("/", "-")
    $path = Join-Path $decisionsDir "$safeTopic.md"
    
    if (Test-Path $path) {
        Write-Host "=== Decision Record: $Topic ==="
        Write-Host ""
        Get-Content $path
    } else {
        Write-Host "No decisions found for topic: $Topic"
        Write-Host "Path checked: $path"
    }
}

function Invoke-Search {
    if (-not $Query) { Write-Error "search requires --query"; return }
    
    Write-Host "Searching decisions for: $Query"
    Write-Host ""
    
    $files = Get-ChildItem $decisionsDir -Filter "*.md" -ErrorAction SilentlyContinue
    $found = $false
    
    foreach ($file in $files) {
        $content = Get-Content $file.FullName -Raw
        if ($content -match $Query) {
            $found = $true
            $topicName = $file.BaseName
            Write-Host "=== $topicName ==="
            # Show matching lines
            $lines = Get-Content $file.FullName
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match $Query) {
                    $start = [Math]::Max(0, $i - 2)
                    $end = [Math]::Min($lines.Count - 1, $i + 2)
                    for ($j = $start; $j -le $end; $j++) {
                        $prefix = if ($j -eq $i) { ">>>" } else { "   " }
                        Write-Host "$prefix $($lines[$j])"
                    }
                    Write-Host ""
                }
            }
        }
    }
    
    if (-not $found) {
        Write-Host "No matches found for: $Query"
    }
}

function Invoke-Snapshot {
    Write-Host "snapshot: Capturing full conversation context..."
    Write-Host "Topic: $Topic" -NoNewline
    if (-not $Topic) { $Topic = "snapshot" }
    
    # Read from stdin if available (piped content)
    $stdinContent = $null
    try {
        if (-not [Console]::IsInputRedirected) {
            Write-Host " (no piped input - capturing as minimal snapshot)"
        } else {
            $stdinContent = $input | Out-String
        }
    } catch {
        Write-Host " (no piped input)"
    }
    
    if ($stdinContent) {
        $stdinContent | & $pythonExe $captureScript --from-stdin --json
    } else {
        & $pythonExe $captureScript --topic $Topic --agent $Agent --json
    }
}

# --- Main Dispatch ---
switch ($Command) {
    "capture"  { Invoke-Capture }
    "decide"   { Invoke-Decide }
    "list"     { Invoke-List }
    "search"   { Invoke-Search }
    "snapshot" { Invoke-Snapshot }
}
