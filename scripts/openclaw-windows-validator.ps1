#!/usr/bin/env pwsh
<#
.SYNOPSIS
    openclaw-windows-validator - Check whether a Windows machine is ready to run OpenClaw.

.DESCRIPTION
    Scans the system for prerequisites and common misconfigurations, then prints
    a color-coded console report and optionally writes an HTML summary.

.PARAMETER HtmlReport
    Path to write an HTML report (default: .\openclaw-validator-report.html).

.PARAMETER NoHtml
    Do not write an HTML report.

.EXAMPLE
    .\openclaw-windows-validator.ps1
    .\openclaw-windows-validator.ps1 -HtmlReport C:\temp\report.html
#>

param(
    [string]$HtmlReport = ".\openclaw-validator-report.html",
    [switch]$NoHtml
)

$ErrorActionPreference = "Stop"

# -------------------------------
# Result tracking
# -------------------------------
$script:checks = @()

function Add-Check($Name, $Status, $Message, $Fix = "") {
    $script:checks += [PSCustomObject]@{
        Name = $Name
        Status = $Status
        Message = $Message
        Fix = $Fix
    }
}

# -------------------------------
# Helpers
# -------------------------------
function Get-CommandPath($Name) {
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Test-Version-AtLeast($Version, $RequiredMajor, $RequiredMinor = 0) {
    if ($Version -match '(\d+)\.(\d+)') {
        $major = [int]$matches[1]
        $minor = [int]$matches[2]
        if ($major -gt $RequiredMajor -or ($major -eq $RequiredMajor -and $minor -ge $RequiredMinor)) {
            return $true
        }
    }
    return $false
}

function Invoke-Command-Quiet($Command, $Arguments) {
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        # If target is a .ps1 script, run it through PowerShell
        if ($Command -like "*.ps1") {
            $psi.FileName = "powershell.exe"
            $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$Command`" $Arguments"
        } else {
            $psi.FileName = $Command
            $psi.Arguments = $Arguments
        }
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        $out = $proc.StandardOutput.ReadToEnd()
        $err = $proc.StandardError.ReadToEnd()
        $proc.WaitForExit(30000) | Out-Null
        return @{ Output = $out; Error = $err; ExitCode = $proc.ExitCode }
    } catch {
        return @{ Output = ""; Error = $_.Exception.Message; ExitCode = -1 }
    }
}

# -------------------------------
# Check 1: Node.js
# -------------------------------
$nodePath = Get-CommandPath "node"
if ($nodePath) {
    $nodeVerOut = Invoke-Command-Quiet $nodePath "--version"
    $nodeVer = ($nodeVerOut.Output + $nodeVerOut.Error).Trim()
    if ($nodeVer -match "v(\d+\.\d+)\.\d+") {
        $nodeVer = $matches[1]
    }
    if (Test-Version-AtLeast $nodeVer 24 0) {
        Add-Check "Node.js" "PASS" "Node.js $nodeVer found at $nodePath (recommended 24+)" ""
    } elseif (Test-Version-AtLeast $nodeVer 22 19) {
        Add-Check "Node.js" "WARN" "Node.js $nodeVer found at $nodePath. OpenClaw works, but 24.x is recommended." "Install Node.js 24.x from nodejs.org or via nvm-windows."
    } else {
        Add-Check "Node.js" "FAIL" "Node.js $nodeVer found at $nodePath. Minimum supported is 22.19." "Install Node.js 24.x."
    }
} else {
    Add-Check "Node.js" "FAIL" "Node.js not found on PATH." "Install Node.js 24.x from https://nodejs.org."
}

# -------------------------------
# Check 2: OpenClaw CLI
# -------------------------------
$ocPath = Get-CommandPath "openclaw"
if ($ocPath) {
    $ocVerOut = Invoke-Command-Quiet $ocPath "--version"
    $ocVer = ($ocVerOut.Output + $ocVerOut.Error).Trim()
    Add-Check "OpenClaw CLI" "PASS" "OpenClaw CLI found at $ocPath. Version: $ocVer" ""
} else {
    Add-Check "OpenClaw CLI" "FAIL" "openclaw command not found on PATH." "Install OpenClaw via Windows Hub or run `iwr -useb https://openclaw.ai/install.ps1 | iex`."
}

# -------------------------------
# Check 3: OpenClaw Gateway status
# -------------------------------
if ($ocPath) {
    $statusOut = Invoke-Command-Quiet $ocPath "status"
    $statusText = ($statusOut.Output + "`n" + $statusOut.Error).Trim()
    $statusLines = $statusText -split "`r?`n" | Select-Object -First 20
    $statusShort = ($statusLines -join "`n").Trim()
    if ($statusOut.ExitCode -eq 0 -and $statusShort -match "running|ok|healthy|reachable|Gateway service") {
        Add-Check "OpenClaw Gateway" "PASS" "Gateway appears healthy.`n$statusShort" ""
    } elseif ($statusShort -match "stopped|not running|inactive") {
        Add-Check "OpenClaw Gateway" "FAIL" "Gateway is not running.`n$statusShort" "Run `openclaw gateway start` or restart Windows Hub."
    } else {
        Add-Check "OpenClaw Gateway" "WARN" "Could not determine Gateway status.`n$statusShort" "Check `openclaw gateway status` manually."
    }
} else {
    Add-Check "OpenClaw Gateway" "FAIL" "Cannot check Gateway because openclaw CLI is missing." "Install OpenClaw first."
}

# -------------------------------
# Check 4: WSL
# -------------------------------
$wslPath = Get-CommandPath "wsl"
if ($wslPath) {
    $wslVerOut = Invoke-Command-Quiet $wslPath "--version"
    if ($wslVerOut.ExitCode -eq 0) {
        $wslVer = $wslVerOut.Output.Trim().Split("`n")[0]
        Add-Check "WSL" "PASS" "WSL installed. $wslVer" ""
    } else {
        Add-Check "WSL" "WARN" "WSL command exists but `wsl --version` failed.`n$($wslVerOut.Error.Trim())" "Run `wsl --update` and reboot if needed."
    }
} else {
    Add-Check "WSL" "FAIL" "WSL not found. Windows Hub's local setup needs WSL2." "Install WSL2: `wsl --install` in an elevated PowerShell, then reboot."
}

# Check for OpenClawGateway distro
if ($wslPath) {
    $wslListOut = Invoke-Command-Quiet $wslPath "--list --verbose"
    if ($wslListOut.Output -match "OpenClawGateway") {
        Add-Check "OpenClaw Gateway Distro" "PASS" "OpenClawGateway WSL distro found." ""
    } else {
        Add-Check "OpenClaw Gateway Distro" "WARN" "OpenClawGateway WSL distro not found. If you installed via Windows Hub, this should exist." "Run Windows Hub setup or reinstall."
    }
}

# -------------------------------
# Check 5: Python (with Microsoft Store alias detection)
# -------------------------------
$pythonPath = Get-CommandPath "python"
$python3Path = Get-CommandPath "python3"

if ($pythonPath) {
    $pyVerOut = Invoke-Command-Quiet $pythonPath "--version"
    $pyVer = ($pyVerOut.Output + $pyVerOut.Error).Trim()
    if ($pythonPath -like "*WindowsApps*") {
        Add-Check "Python" "WARN" "`python` resolves to the Microsoft Store stub at $pythonPath. This is not a real interpreter." "Install Python from python.org or use `python3` if a real install exists."
    } else {
        Add-Check "Python" "PASS" "Real Python found at $pythonPath. $pyVer" ""
    }
} else {
    Add-Check "Python" "WARN" "`python` not found. Optional for basic OpenClaw, but required for the memory continuity scripts." "Install Python 3.12+ from https://python.org."
}

if ($python3Path -and $python3Path -like "*WindowsApps*") {
    Add-Check "Python3 Alias" "WARN" "`python3` resolves to the Microsoft Store stub. The memory scripts use a resolver to avoid this, but it can confuse other tools." "Use the `scripts/find-python.ps1` resolver or install Python from python.org."
} elseif ($python3Path) {
    Add-Check "Python3 Alias" "PASS" "`python3` resolves to a real interpreter at $python3Path." ""
} else {
    Add-Check "Python3 Alias" "WARN" "`python3` not found." "Use `python` or install Python and ensure it is on PATH."
}

# -------------------------------
# Check 6: Ollama
# -------------------------------
$ollamaPath = Get-CommandPath "ollama"
if ($ollamaPath) {
    $ollamaVerOut = Invoke-Command-Quiet $ollamaPath "--version"
    $ollamaVer = ($ollamaVerOut.Output + $ollamaVerOut.Error).Trim()
    Add-Check "Ollama CLI" "PASS" "Ollama found at $ollamaPath. $ollamaVer" ""

    try {
        $resp = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method Get -TimeoutSec 5 -ErrorAction Stop
        $models = $resp.models | Select-Object -ExpandProperty name
        if ($models.Count -gt 0) {
            Add-Check "Ollama Server" "PASS" "Ollama server is running with $($models.Count) model(s): $($models -join ', ')." ""
        } else {
            Add-Check "Ollama Server" "WARN" "Ollama server is running but has no models pulled." "Pull a model: `ollama pull kimi-k2.7-code:cloud` or another model."
        }
    } catch {
        Add-Check "Ollama Server" "WARN" "Ollama CLI exists but the server at localhost:11434 is not responding." "Start Ollama from the Start menu or run `ollama serve`."
    }
} else {
    Add-Check "Ollama" "FAIL" "Ollama not found on PATH." "Install Ollama from https://ollama.com."
}

# -------------------------------
# Check 7: Gateway port
# -------------------------------
try {
    $portTest = Test-NetConnection -ComputerName localhost -Port 18789 -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
    if ($portTest.TcpTestSucceeded) {
        Add-Check "Gateway Port (18789)" "PASS" "Port 18789 is reachable on localhost." ""
    } else {
        Add-Check "Gateway Port (18789)" "WARN" "Port 18789 is not reachable on localhost. The Gateway may not be running or may use a different port." "Check `openclaw config get gateway.port` and confirm the Gateway is started."
    }
} catch {
    Add-Check "Gateway Port (18789)" "WARN" "Could not test port 18789: $_" ""
}

# -------------------------------
# Check 8: OpenClaw config file
# -------------------------------
$configPath = Join-Path $env:USERPROFILE ".openclaw\openclaw.json"
if (Test-Path $configPath) {
    Add-Check "OpenClaw Config" "PASS" "Config file found at $configPath." ""
} else {
    Add-Check "OpenClaw Config" "WARN" "Config file not found at $configPath. This is normal before onboarding." "Run `openclaw onboard` or open Windows Hub to configure."
}

# -------------------------------
# Console report
# -------------------------------
function Write-StatusLine($Status, $Name, $Message, $Fix) {
    switch ($Status) {
        "PASS" { $color = "Green"; $icon = "OK " }
        "WARN" { $color = "Yellow"; $icon = "! " }
        "FAIL" { $color = "Red"; $icon = "X " }
        default { $color = "White"; $icon = "  " }
    }
    Write-Host "[$icon] $Name" -ForegroundColor $color -NoNewline
    Write-Host " - $Message" -ForegroundColor Gray
    if ($Fix) {
        Write-Host "    Fix: $Fix" -ForegroundColor Cyan
    }
}

Write-Host ""
Write-Host "=============================================="
Write-Host "  OpenClaw Windows Setup Validator"
Write-Host "=============================================="
Write-Host ""

$pass = 0
$warn = 0
$fail = 0
foreach ($check in $script:checks) {
    Write-StatusLine $check.Status $check.Name $check.Message $check.Fix
    switch ($check.Status) {
        "PASS" { $pass++ }
        "WARN" { $warn++ }
        "FAIL" { $fail++ }
    }
}

Write-Host ""
Write-Host "----------------------------------------------"
$resultColor = if ($fail -gt 0) { "Red" } elseif ($warn -gt 0) { "Yellow" } else { "Green" }
Write-Host "Result: $pass passed, $warn warnings, $fail failures ($($script:checks.Count) checks total)" -ForegroundColor $resultColor
Write-Host "----------------------------------------------"

# -------------------------------
# HTML report
# -------------------------------
if (-not $NoHtml) {
    Add-Type -AssemblyName System.Web -ErrorAction SilentlyContinue | Out-Null
    $rows = foreach ($check in $script:checks) {
        $statusClass = $check.Status.ToLower()
        $nameHtml = [System.Web.HttpUtility]::HtmlEncode($check.Name)
        $msgHtml = [System.Web.HttpUtility]::HtmlEncode($check.Message).Replace("`n", "<br>")
        $fixHtml = [System.Web.HttpUtility]::HtmlEncode($check.Fix).Replace("`n", "<br>")
        "<tr class='status-$statusClass'><td>$($check.Status)</td><td>$nameHtml</td><td>$msgHtml</td><td>$fixHtml</td></tr>"
    }
    $rowsHtml = $rows -join "`n"
    $reportTime = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $computer = $env:COMPUTERNAME

    $html = @"
<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='UTF-8'>
  <title>OpenClaw Windows Setup Validator Report</title>
  <style>
    body { font-family: system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 2rem; background: #0f172a; color: #e2e8f0; }
    h1 { color: #38bdf8; }
    table { width: 100%; border-collapse: collapse; margin-top: 1rem; }
    th, td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #334155; }
    th { background: #1e293b; color: #94a3b8; }
    .status-pass { color: #4ade80; font-weight: bold; }
    .status-warn { color: #facc15; font-weight: bold; }
    .status-fail { color: #f87171; font-weight: bold; }
    .summary { margin-top: 1.5rem; font-size: 1.1rem; }
    .footer { margin-top: 2rem; color: #64748b; font-size: 0.85rem; }
  </style>
</head>
<body>
  <h1>OpenClaw Windows Setup Validator Report</h1>
  <p>Generated: $reportTime on $computer</p>
  <table>
    <thead>
      <tr><th>Status</th><th>Check</th><th>Details</th><th>Suggested Fix</th></tr>
    </thead>
    <tbody>
$rowsHtml
    </tbody>
  </table>
  <div class='summary'><strong>Summary:</strong> $pass passed, $warn warnings, $fail failures ($($script:checks.Count) checks total)</div>
  <div class='footer'>Report generated by scripts/openclaw-windows-validator.ps1 - SMF Works Project</div>
</body>
</html>
"@

    $reportFullPath = [System.IO.Path]::GetFullPath($HtmlReport)
    [System.IO.File]::WriteAllText($reportFullPath, $html, [System.Text.Encoding]::UTF8)
    Write-Host ""
    Write-Host "HTML report written to: $reportFullPath" -ForegroundColor Green
}

if ($fail -gt 0) { exit 1 } else { exit 0 }
