# OpenClaw Companion App for Windows

A native Windows system tray application for managing the OpenClaw gateway process.

## Features

### Phase 1 — Core Gateway Management ✅
- **System tray integration** — Icon with context menu (Show, Start, Stop, Restart, Exit)
- **Gateway status detection** — Real-time monitoring via WMI + HTTP health checks
- **Process controls** — Start, Stop, Restart gateway with a single click
- **Process info display** — PID, Memory usage, Uptime, CPU percentage
- **Live log viewer** — Scrollable, auto-scrolling log of gateway events
- **Native notifications** — Windows toast notifications on status changes
- **Single instance** — Mutex-enforced, prevents multiple app instances
- **Dark theme** — Modern WPF dark UI

### Phase 2 — Settings & Configuration ✅
- **Settings panel** — Configure gateway host, port, poll interval
- **Auto-start with Windows** — Registry Run key integration
- **Minimize to tray on startup** — Start hidden in system tray
- **Close-to-tray behavior** — X button minimizes instead of quitting
- **Settings persistence** — Save/load preferences to JSON config
- **JSON config editing** — Edit `openclaw.json` directly from the app (read-only viewer + edit mode with backup)

### Phase 3 — Advanced Features ✅
- **QR code pairing** — Generate QR codes for mobile node pairing
- **CLI integration** — Run OpenClaw CLI commands from the app
- **Auto-restart on crash** — Detect gateway failure and automatically restart
- **PATH diagnostics** — Check Node.js, OpenClaw PATH configuration
- **Update check** — Check for new OpenClaw releases from GitHub

## Tech Stack

- **WPF** with .NET 8
- **CommunityToolkit.Mvvm** for MVVM
- **System.Management (WMI)** for process queries
- **Windows Forms NotifyIcon** for system tray
- **PowerShell** for toast notifications

## Installation

Download the latest portable executable from [Releases](../../releases) or build from source:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

## Development

```bash
dotnet build
dotnet run
```

## Project Structure

```
OpenClawCompanion/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── ViewModels/
│   └── MainViewModel.cs
├── Services/
│   ├── GatewayService.cs
│   ├── TrayIconService.cs
│   ├── NotificationService.cs
│   └── Logger.cs
├── Models/
│   ├── GatewayStatus.cs
│   └── ProcessInfo.cs
└── Converters/
    └── StatusColorConverter.cs
```

## License

MIT — SMF Works Project
