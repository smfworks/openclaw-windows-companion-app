$ErrorActionPreference = "Stop"

$workspace = "C:\Users\Michael Gannotti\.openclaw\workspace"
$pipeline = Join-Path $workspace "scripts\memory-pipeline.ps1"
$autoCapture = Join-Path $workspace "scripts\session-auto-capture.ps1"

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable

# Safety net: auto-capture recent conversation every 30 minutes
$autoAction = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$autoCapture`"" `
    -WorkingDirectory $workspace

$startAt = (Get-Date).AddMinutes(2)
$autoTrigger = New-ScheduledTaskTrigger -Once -At $startAt `
    -RepetitionInterval (New-TimeSpan -Minutes 30) `
    -RepetitionDuration (New-TimeSpan -Days 3650)

Register-ScheduledTask -TaskName "SMFWorks-Memory-SafetyNet" `
    -TaskPath "\SMFWorks\" `
    -Action $autoAction `
    -Trigger $autoTrigger `
    -Settings $settings `
    -Description "SMF Works token-free conversational memory safety net: exports session trajectory and runs local Ollama extraction every 30 minutes" `
    -Force

Write-Host "Created: SMFWorks-Memory-SafetyNet (next run at $startAt)"

# Daily promotion task at 2:15 AM ET
$promoAction = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$pipeline`"" `
    -WorkingDirectory $workspace

$promoTrigger = New-ScheduledTaskTrigger -Daily -At "02:15"

Register-ScheduledTask -TaskName "SMFWorks-Memory-Promotion" `
    -TaskPath "\SMFWorks\" `
    -Action $promoAction `
    -Trigger $promoTrigger `
    -Settings $settings `
    -Description "SMF Works nightly memory promotion scan at 2:15 AM ET" `
    -Force

Write-Host "Created: SMFWorks-Memory-Promotion (daily at 02:15 AM)"
