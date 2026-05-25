using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenClawCompanion.Models;
using OpenClawCompanion.Services;

namespace OpenClawCompanion.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GatewayService _gatewayService;
    private readonly System.Timers.Timer _pollTimer;
    private readonly object _pollLock = new();

    private bool _isPolling;
    private int _consecutiveDownCount = 0;
    private const int ConsecutiveDownThreshold = 2;

    [ObservableProperty]
    private GatewayStatus _gatewayStatus = GatewayStatus.Stopped;

    [ObservableProperty]
    private string _statusText = "Stopped";

    [ObservableProperty]
    private string _statusColor = "#E74856";

    [ObservableProperty]
    private string _pidText = "N/A";

    [ObservableProperty]
    private string _memoryText = "N/A";

    [ObservableProperty]
    private string _uptimeText = "N/A";

    [ObservableProperty]
    private string _cpuText = "N/A";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isStarting;

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _gatewayHost = "localhost";

    [ObservableProperty]
    private string _gatewayPort = "18789";

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private int _pollIntervalSeconds = 10;

    public ObservableCollection<string> LogEntries { get; } = new();

    public event Action<GatewayStatus>? StatusChanged;
    public event Action? ShowWindowRequested;

    public MainViewModel(GatewayService gatewayService)
    {
        _gatewayService = gatewayService;

        _pollTimer = new System.Timers.Timer(PollIntervalSeconds * 1000);
        _pollTimer.Elapsed += async (_, _) => await PollStatusAsync();
        _pollTimer.AutoReset = true;
    }

    [RelayCommand]
    private async Task StartGatewayAsync()
    {
        if (IsRunning || IsStarting) return;

        try
        {
            GatewayStatus = GatewayStatus.Starting;
            UpdateStatusDisplay();
            AppendLog("Starting gateway...");
            Logger.Info("Start command issued");

            var success = await _gatewayService.StartAsync();
            if (success)
            {
                GatewayStatus = GatewayStatus.Running;
                IsRunning = true;
                AppendLog("Gateway started successfully");
                NotificationService.Show("OpenClaw Companion", "Gateway started successfully");
                StartPolling();
            }
            else
            {
                GatewayStatus = GatewayStatus.Error;
                AppendLog("ERROR: Gateway failed to start");
                NotificationService.Show("OpenClaw Companion", "Gateway failed to start");
                Logger.Error("Gateway start failed");
            }
        }
        catch (Exception ex)
        {
            GatewayStatus = GatewayStatus.Error;
            AppendLog($"ERROR: Start exception - {ex.Message}");
            NotificationService.Show("OpenClaw Companion", $"Gateway start error: {ex.Message}");
            Logger.Error($"Start gateway exception: {ex}");
        }

        UpdateStatusDisplay();
        StatusChanged?.Invoke(GatewayStatus);
    }

    [RelayCommand]
    private async Task StopGatewayAsync()
    {
        if (!IsRunning) return;

        try
        {
            AppendLog("Stopping gateway...");
            Logger.Info("Stop command issued");

            var stopped = await _gatewayService.StopAsync();
            StopPolling();
            if (stopped)
            {
                GatewayStatus = GatewayStatus.Stopped;
                IsRunning = false;
                ClearProcessInfo();
                UpdateStatusDisplay();
                AppendLog("Gateway stopped");
                NotificationService.Show("OpenClaw Companion", "Gateway stopped");
            }
            else
            {
                GatewayStatus = GatewayStatus.Error;
                UpdateStatusDisplay();
                AppendLog("ERROR: Gateway stop failed — process may still be running");
                NotificationService.Show("OpenClaw Companion", "Gateway stop failed — process may still be running");
            }
        }
        catch (Exception ex)
        {
            GatewayStatus = GatewayStatus.Error;
            AppendLog($"ERROR: Stop exception - {ex.Message}");
            NotificationService.Show("OpenClaw Companion", $"Gateway stop error: {ex.Message}");
            Logger.Error($"Stop gateway exception: {ex}");
        }

        StatusChanged?.Invoke(GatewayStatus);
    }

    [RelayCommand]
    private async Task RestartGatewayAsync()
    {
        try
        {
            AppendLog("Restarting gateway...");
            Logger.Info("Restart command issued");

            await StopGatewayAsync();

            // Poll port for up to 5 seconds waiting for gateway to be fully stopped
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(500);
                if (!IsRunning)
                    break;
            }

            await StartGatewayAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: Restart exception - {ex.Message}");
            NotificationService.Show("OpenClaw Companion", $"Gateway restart error: {ex.Message}");
            Logger.Error($"Restart gateway exception: {ex}");
        }
    }

    [RelayCommand]
    private void ToggleWindow()
    {
        ShowWindowRequested?.Invoke();
    }

    public void StartPolling()
    {
        lock (_pollLock)
        {
            if (_isPolling) return;
            _isPolling = true;
            _pollTimer.Interval = PollIntervalSeconds * 1000;
            _pollTimer.Start();
            Logger.Info($"Polling started (interval: {PollIntervalSeconds}s)");
        }
    }

    public void StopPolling()
    {
        lock (_pollLock)
        {
            if (!_isPolling) return;
            _isPolling = false;
            _pollTimer.Stop();
            Logger.Info("Polling stopped");
        }
    }

    public async Task PollStatusAsync()
    {
        lock (_pollLock)
        {
            if (!_isPolling) return;
        }

        try
        {
            var status = await _gatewayService.GetStatusAsync();

            // Ensure we are on the UI thread for property updates
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (status == GatewayStatus.Running)
                {
                    _consecutiveDownCount = 0;
                    if (GatewayStatus != GatewayStatus.Running)
                    {
                        GatewayStatus = GatewayStatus.Running;
                        IsRunning = true;
                        UpdateProcessInfo();
                        Logger.Info("Poll: Gateway is running");
                        UpdateStatusDisplay();
                        StatusChanged?.Invoke(GatewayStatus);
                    }
                    else
                    {
                        UpdateProcessInfo();
                    }
                }
                else
                {
                    _consecutiveDownCount++;
                    if (_consecutiveDownCount >= ConsecutiveDownThreshold && GatewayStatus != GatewayStatus.Stopped)
                    {
                        var previousStatus = GatewayStatus;
                        GatewayStatus = GatewayStatus.Stopped;
                        IsRunning = false;
                        ClearProcessInfo();
                        UpdateStatusDisplay();
                        StatusChanged?.Invoke(GatewayStatus);

                        if (previousStatus == GatewayStatus.Running)
                        {
                            AppendLog("WARNING: Gateway stopped unexpectedly");
                            NotificationService.Show("OpenClaw Companion", "Gateway stopped unexpectedly");
                            Logger.Warn("Gateway stopped unexpectedly (detected via poll)");
                        }
                    }
                    else if (_consecutiveDownCount < ConsecutiveDownThreshold)
                    {
                        Logger.Info($"Poll: Gateway check failed ({_consecutiveDownCount}/{ConsecutiveDownThreshold}) — waiting for confirmation");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Poll error: {ex.Message}");
        }
    }

    private void UpdateStatusDisplay()
    {
        switch (GatewayStatus)
        {
            case GatewayStatus.Running:
                StatusText = "Running";
                StatusColor = "#16C60C";
                break;
            case GatewayStatus.Stopped:
                StatusText = "Stopped";
                StatusColor = "#E74856";
                break;
            case GatewayStatus.Starting:
                StatusText = "Starting...";
                StatusColor = "#F9F1A5";
                break;
            case GatewayStatus.Error:
                StatusText = "Error";
                StatusColor = "#E74856";
                break;
        }
    }

    private void UpdateProcessInfo()
    {
        var info = _gatewayService.GetProcessInfo();
        if (info != null)
        {
            PidText = info.Pid.ToString();
            MemoryText = $"{info.MemoryMB} MB";
            UptimeText = info.Uptime;
            CpuText = $"{info.CpuPercent}%";
        }
    }

    private void ClearProcessInfo()
    {
        PidText = "N/A";
        MemoryText = "N/A";
        UptimeText = "N/A";
        CpuText = "N/A";
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var entry = $"[{timestamp}] {message}";

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            LogEntries.Add(entry);
            // Keep only last 500 entries
            while (LogEntries.Count > 500)
                LogEntries.RemoveAt(0);

            LogText = string.Join(Environment.NewLine, LogEntries);
        });
    }

    public async Task InitializeAsync()
    {
        AppendLog("OpenClaw Companion started");
        AppendLog($"Log file: {Logger.GetLogPath()}");

        // Check actual gateway status at startup
        var status = await _gatewayService.GetStatusAsync();
        if (status == GatewayStatus.Running)
        {
            GatewayStatus = GatewayStatus.Running;
            IsRunning = true;
            UpdateProcessInfo();
            UpdateStatusDisplay();
            AppendLog("Gateway detected as running");
            StartPolling();
            StatusChanged?.Invoke(GatewayStatus);
        }
        else if (AutoStart)
        {
            AppendLog("Auto-start is enabled, starting gateway...");
            await StartGatewayAsync();
        }
        else
        {
            GatewayStatus = GatewayStatus.Stopped;
            UpdateStatusDisplay();
            StatusChanged?.Invoke(GatewayStatus);
        }
    }
}
