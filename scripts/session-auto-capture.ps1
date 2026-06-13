#!/usr/bin/env pwsh
<#
.SYNOPSIS
    session-auto-capture — Token-free conversational memory capture for SMF Works.

.DESCRIPTION
    Exports the main session trajectory, pulls recent human/assistant messages,
    sends them to the local Ollama model, and writes structured memory via
    session-capture.py. Designed to run from Windows Task Scheduler every 30 min.

.PARAMETER MinutesBack
    How many minutes of recent conversation to analyze (default: 35, slightly
    longer than the 30-min schedule to avoid gaps).

.PARAMETER MinSignificance
    Minimum significance score (1-10) required before writing memory (default: 5).

.PARAMETER SessionKey
    OpenClaw session key to export (default: agent:main:main).

.PARAMETER Model
    Local Ollama model to use for extraction (default: kimi-k2.7-code:cloud).

.EXAMPLE
    .\session-auto-capture.ps1
    .\session-auto-capture.ps1 -MinutesBack 60
#>

param(
    [int]$MinutesBack = 35,
    [int]$MinSignificance = 5,
    [string]$SessionKey = "agent:main:main",
    [string]$Model = "kimi-k2.7-code:cloud",
    [switch]$RetainTemp,
    [int]$KeepTrajectoryExports = 5
)

$ErrorActionPreference = "Stop"

$workspace = if ($env:OPENCLAW_WORKSPACE) { $env:OPENCLAW_WORKSPACE } else { "C:\Users\Michael Gannotti\.openclaw\workspace" }
$scriptsDir = Join-Path $workspace "scripts"
$memoryDir = Join-Path $workspace "memory"
$logFile = Join-Path $memoryDir "auto-capture.log"

function Write-Log($Message) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message
    Add-Content -Path $logFile -Value $line -ErrorAction SilentlyContinue
    Write-Host $line
}

if (-not (Test-Path $memoryDir)) { New-Item -ItemType Directory -Path $memoryDir -Force | Out-Null }
Write-Log "Auto-capture starting for session $SessionKey"

# 1. Resolve Python
$findPython = Join-Path $scriptsDir "find-python.ps1"
try {
    $pythonExe = & $findPython
    Write-Log "Python: $pythonExe"
} catch {
    Write-Log "FATAL: Could not resolve Python: $_"
    exit 1
}

# 2. Export trajectory
$exportName = "auto-capture-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
try {
    Write-Log "Exporting trajectory..."
    $export = openclaw sessions export-trajectory --session-key $SessionKey --output $exportName --json 2>&1 | ConvertFrom-Json
    $exportDir = $export.outputDir
    Write-Log "Exported to $exportDir"
} catch {
    Write-Log "FATAL: Could not export trajectory: $_"
    exit 2
}

# 3. Read session-branch.json and extract recent messages
$branchFile = Join-Path $exportDir "session-branch.json"
try {
    $branch = Get-Content $branchFile -Raw | ConvertFrom-Json
} catch {
    Write-Log "FATAL: Could not parse session-branch.json: $_"
    exit 3
}

$cutoff = (Get-Date).AddMinutes(-$MinutesBack).ToUniversalTime()
$recentMessages = @()

foreach ($entry in $branch.entries) {
    if ($entry.type -ne "message") { continue }
    $msg = $entry.message
    if (-not $msg -or -not $msg.content) { continue }
    # Only include actual user/assistant text, not tool/system/runtime metadata
    if ($msg.role -notin @("user", "assistant")) { continue }
    $msgTime = if ($entry.timestamp) { [datetime]::Parse($entry.timestamp).ToUniversalTime() } else { $cutoff.AddMinutes(1) }
    if ($msgTime -lt $cutoff) { continue }
    $role = $msg.role
    $content = $msg.content -replace "\r\n", "\n"
    # Truncate very long messages (e.g., tool output dumps)
    $maxLen = 1500
    if ($content.Length -gt $maxLen) {
        $content = $content.Substring(0, $maxLen) + "\n[...truncated...]"
    }
    $recentMessages += "[$role]: $content"
    # Limit total messages to avoid huge prompts
    if ($recentMessages.Count -ge 40) {
        $recentMessages = $recentMessages | Select-Object -Last 40
    }
}

if ($recentMessages.Count -eq 0) {
    Write-Log "No messages in last $MinutesBack minutes. Nothing to capture."
    exit 0
}

$conversation = $recentMessages -join "\n\n"
Write-Log "Found $($recentMessages.Count) recent messages"

# 4. Load previous capture summary to avoid re-capturing the same decisions
$stateFile = Join-Path $memoryDir "auto-capture-state.json"
$previousTopics = @()
if (Test-Path $stateFile) {
    try {
        $state = Get-Content $stateFile -Raw | ConvertFrom-Json
        $previousTopics = $state.previousTopics
        $lastCaptureTime = if ($state.lastCaptureTime) { [datetime]::Parse($state.lastCaptureTime).ToUniversalTime() } else { [datetime]::MinValue }
    } catch {
        Write-Log "Could not read state file: $_"
    }
}

# 5. Build extraction prompt
$prompt = @"
You are a memory extraction assistant. Review the following recent conversation between a human (user) and an AI assistant. Identify only NEW significant information worth remembering long-term.

Output strictly as JSON with this structure:
{
  "topic": "short topic slug (e.g., memory-continuity, sparkforge-v2, blog-openclaw-windows)",
  "significance": 0-10,
  "decisions": ["decision 1", "decision 2"],
  "actions": ["action 1", "action 2"],
  "architectures": ["design choice 1"],
  "dependencies": ["context dependency 1"],
  "open_threads": ["open question or follow-up 1"]
}

Significance scoring rubric:
- 0-2: Routine chat, greetings, clarifications, or no durable information.
- 3-5: Useful context or minor updates, but not critical to remember.
- 6-7: Real decisions, committed actions, or meaningful design choices.
- 8-10: Major strategic decisions, architectural commitments, or important unresolved questions.

Rules:
- If nothing significant happened, set significance to 0-2 and all arrays empty.
- Decisions = explicit choices made or agreed upon in THIS conversation segment.
- Actions = things actually done or committed to in THIS segment.
- Do NOT recapture decisions or actions that were merely confirmed or referenced from earlier in the conversation.
- If the conversation only revisits or confirms earlier decisions, return significance 0 and empty arrays.
- Keep values concise and factual. Use 3-10 words per item when possible.
- Avoid duplicating the same decision phrased slightly differently.

Conversation:
$conversation
"@

# 6. Call local Ollama via Python for robust JSON/HTTP handling
$ollamaModel = $Model
$ollamaUrl = "http://localhost:11434/api/generate"
$promptFile = Join-Path $memoryDir "auto-capture-prompt.txt"
[System.IO.File]::WriteAllText($promptFile, $prompt, [System.Text.Encoding]::UTF8)

$pythonOllamaScript = @"
import json, sys, urllib.request, urllib.error

url = sys.argv[1]
model = sys.argv[2]
prompt_file = sys.argv[3]

with open(prompt_file, 'r', encoding='utf-8') as f:
    prompt = f.read()

payload = {
    'model': model,
    'prompt': prompt,
    'stream': False,
    'options': {'temperature': 0.1, 'num_predict': 2048}
}
body = json.dumps(payload, ensure_ascii=False).encode('utf-8')
req = urllib.request.Request(url, data=body, headers={'Content-Type': 'application/json'}, method='POST')
try:
    with urllib.request.urlopen(req, timeout=120) as resp:
        data = json.loads(resp.read().decode('utf-8'))
        print('OLLAMA_OK')
        print(data.get('response', ''))
except urllib.error.HTTPError as e:
    print('OLLAMA_ERROR', e.code, e.read().decode('utf-8', errors='replace'), file=sys.stderr)
    sys.exit(1)
except Exception as e:
    print('OLLAMA_ERROR', str(e), file=sys.stderr)
    sys.exit(1)
"@

$pythonScriptPath = Join-Path $memoryDir "auto-capture-ollama-call.py"
[System.IO.File]::WriteAllText($pythonScriptPath, $pythonOllamaScript, [System.Text.Encoding]::UTF8)

try {
    Write-Log "Calling Ollama model $Model..."
    $ollamaOutput = & $pythonExe $pythonScriptPath $ollamaUrl $ollamaModel $promptFile 2>&1 | Out-String
    $lines = $ollamaOutput -split "`r?`n"
    $status = $lines[0]
    $raw = ($lines | Select-Object -Skip 1) -join "`n"
    if ($status -ne "OLLAMA_OK") {
        throw "Ollama call failed: $ollamaOutput"
    }
    $rawResponseFile = Join-Path $memoryDir "auto-capture-raw-response.json"
    [System.IO.File]::WriteAllText($rawResponseFile, $raw, [System.Text.Encoding]::UTF8)
    Write-Log "Ollama raw response length: $($raw.Length); saved to $rawResponseFile"
} catch {
    Write-Log "FATAL: Ollama call failed: $_"
    exit 4
}

# 7. Extract JSON from response
$jsonMatch = [regex]::Match($raw, '\{[\s\S]*\}')
if (-not $jsonMatch.Success) {
    Write-Log "No JSON object found in Ollama response. Skipping capture."
    Write-Log "Raw: $raw"
    exit 0
}

try {
    $jsonText = $jsonMatch.Value
    Write-Log "Extracted JSON length: $($jsonText.Length)"
    $data = $jsonText | ConvertFrom-Json
    Write-Log "Parsed significance: $($data.significance); decisions count: $($data.decisions.Count)"
} catch {
    Write-Log "Could not parse JSON: $_"
    Write-Log "Raw JSON: $jsonText"
    exit 5
}

if ($data.significance -lt $MinSignificance) {
    Write-Log "Significance $($data.significance) below threshold $MinSignificance. Skipping capture."
    exit 0
}

# 8. Build arguments for session-capture.py
$captureScript = Join-Path $scriptsDir "session-capture.py"
$captureArgs = @($captureScript, "--topic", $data.topic, "--agent", "Jeff", "--json")

function Add-CaptureArg($Flag, $List) {
    if ($List -and $List.Count -gt 0) {
        $joined = ($List | ForEach-Object { $_ -replace ',', ';' }) -join ","
        $script:captureArgs += $Flag
        $script:captureArgs += $joined
    }
}

Add-CaptureArg "--decisions" $data.decisions
Add-CaptureArg "--actions" $data.actions
Add-CaptureArg "--architectures" $data.architectures
Add-CaptureArg "--dependencies" $data.dependencies
Add-CaptureArg "--open-threads" $data.open_threads

try {
    Write-Log "Writing capture to memory..."
    Write-Log "Capture args: $($captureArgs -join ' ')"
    $capOut = & $pythonExe $captureArgs 2>&1 | Out-String
    Write-Log "Capture output: $capOut"
} catch {
    Write-Log "ERROR writing capture: $_"
    exit 6
}

Write-Log "Auto-capture completed for topic: $($data.topic)"

# 9. Save capture state for deduplication and skip-if-no-new-messages
try {
    $newState = @{
        lastCaptureTime = (Get-Date).ToUniversalTime().ToString("o")
        previousTopics = @($data.topic) + ($previousTopics | Select-Object -First 10)
    } | ConvertTo-Json -Depth 3
    [System.IO.File]::WriteAllText($stateFile, $newState, [System.Text.Encoding]::UTF8)
    Write-Log "Saved capture state"
} catch {
    Write-Log "WARN: Could not save state file: $_"
}

# 10. Clean up temporary files and old trajectory exports (unless -RetainTemp)
if (-not $RetainTemp) {
    $tempFiles = @($promptFile, $pythonScriptPath, $rawResponseFile)
    foreach ($f in $tempFiles) {
        if ($f -and (Test-Path $f)) {
            Remove-Item $f -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Log "Cleaned up temporary files"

    $exportRoot = Join-Path $workspace ".openclaw\trajectory-exports"
    if (Test-Path $exportRoot) {
        $oldExports = Get-ChildItem $exportRoot -Directory -Filter "auto-capture-*" | Sort-Object LastWriteTime -Descending | Select-Object -Skip $KeepTrajectoryExports
        foreach ($dir in $oldExports) {
            Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
            Write-Log "Removed old trajectory export: $($dir.Name)"
        }
    }
}

exit 0
