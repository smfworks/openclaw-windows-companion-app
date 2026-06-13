#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Resolves a usable Python interpreter on Windows, avoiding broken aliases.

.DESCRIPTION
    Microsoft ships a "python3.exe" App Execution Alias that opens the Store
    when Python isn't installed from there. This script finds a real python.exe
    (preferring the one on PATH that reports a version) and returns its path.

    Exit code 0 on success, 1 on failure.

.EXAMPLE
    .\find-python.ps1
    C:\Users\...\python.exe
#>

$ErrorActionPreference = "Stop"

function Test-Python($Path) {
    if (-not (Test-Path $Path)) { return $false }
    try {
        $ver = & $Path --version 2>&1
        if ($ver -match "Python \d+\.\d+\.\d+") {
            return $true
        }
    } catch {}
    return $false
}

# 1. Try 'python' first (often a real install on Windows)
$pythonCmd = Get-Command "python" -ErrorAction SilentlyContinue
if ($pythonCmd -and (Test-Python $pythonCmd.Source)) {
    Write-Output $pythonCmd.Source
    exit 0
}

# 2. Try py.exe launcher
$pyCmd = Get-Command "py" -ErrorAction SilentlyContinue
if ($pyCmd) {
    try {
        $path = & $pyCmd -c "import sys; print(sys.executable)" 2>&1
        if ($path -and (Test-Python $path)) {
            Write-Output $path
            exit 0
        }
    } catch {}
}

# 3. Search common install locations
$commonPaths = @(
    "${env:LOCALAPPDATA}\Programs\Python\Python*\python.exe"
    "${env:PROGRAMFILES}\Python*\python.exe"
    "${env:PROGRAMFILES(x86)}\Python*\python.exe"
    "C:\Python*\python.exe"
)

foreach ($pattern in $commonPaths) {
    $matches = Get-Item $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending
    foreach ($m in $matches) {
        if (Test-Python $m.FullName) {
            Write-Output $m.FullName
            exit 0
        }
    }
}

# 4. Last resort: where.exe python (avoid python3 alias)
try {
    $wherePython = (& where.exe python) | Select-Object -First 1
    if ($wherePython -and (Test-Python $wherePython)) {
        Write-Output $wherePython
        exit 0
    }
} catch {}

Write-Error "No usable Python interpreter found. Install Python from python.org or Microsoft Store and ensure 'python --version' works."
exit 1
