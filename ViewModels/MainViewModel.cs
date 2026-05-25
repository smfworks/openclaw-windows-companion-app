using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QRCoder;
using OpenClawCompanion.Models;
using OpenClawCompanion.Services;

namespace OpenClawCompanion.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GatewayService _gatewayService;
    private readonly SettingsService _settingsService;
    private readonly CliService _cliService;
    private readonly DiagnosticsService _diagnosticsService;
    private readonly UpdateService _updateService;
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

    // Settings backing fields
    [ObservableProperty]
    private string _gatewayHost = "localhost";

    [ObservableProperty]
    private string _gatewayPort = "18789";

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _autoRestart = true;

    [ObservableProperty]
    private int _pollIntervalSeconds = 10;

    [ObservableProperty]
    private bool _startMinimized = true;

    [ObservableProperty]
    private bool _startWithWindows = false;

    // Settings panel state
    [ObservableProperty]
    private bool _isSettingsVisible;

    // Config editor state
    [ObservableProperty]
    private bool _isConfigEditorVisible;

    [ObservableProperty]
    private bool _isConfigEditing;

    [ObservableProperty]
    private string _configJsonText = string.Empty;

    [ObservableProperty]
    private string _configFilePath = string.Empty;

    // CLI state
    [ObservableProperty]
    private bool _isCliVisible;

    [ObservableProperty]
    private string _cliCommandInput = string.Empty;

    [ObservableProperty]
    private string _cliOutputText = string.Empty;

    [ObservableProperty]
    private bool _isCliExecuting;

    private int _cliHistoryIndex = -1;
    private string _cliSavedInput = string.Empty;

    // Diagnostics state
    [ObservableProperty]
    private bool _isDiagnosticsVisible;

    [ObservableProperty]
    private bool _isDiagnosticsRunning;

    [ObservableProperty]
    private string _diagnosticsStatusText = string.Empty;

    // Update check state
    [ObservableProperty]
    private bool _isUpdateCheckRunning;

    [ObservableProperty]
    private UpdateInfo? _updateInfo;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    // Editable settings (separate from live settings until saved)
    [ObservableProperty]
    private string _settingsGatewayHost = "localhost";

    [ObservableProperty]
    private string _settingsGatewayPort = "18789";

    [ObservableProperty]
    private int _settingsPollIntervalSeconds = 10;

    [ObservableProperty]
    private bool _settingsAutoStart;

    [ObservableProperty]
    private bool _settingsAutoRestart = true;

    [ObservableProperty]
    private bool _settingsStartMinimized = true;

    [ObservableProperty]
    private bool _settingsStartWithWindows;

    public ObservableCollection<string> LogEntries { get; } = new();
    public ObservableCollection<CliCommandOutput> CliHistory { get; }
    public ObservableCollection<DiagnosticsCheck> DiagnosticsResults { get; } = new();

    public event Action<GatewayStatus>? StatusChanged;
    public event Action? ShowWindowRequested;
    public event Action<BitmapImage, string>? ShowQRCodeRequested;

    public MainViewModel(GatewayService gatewayService, SettingsService settingsService)
    {
        _gatewayService = gatewayService;
        _settingsService = settingsService;
        _cliService = new CliService();
        _diagnosticsService = new DiagnosticsService();
        _updateService = new UpdateService();
        CliHistory = _cliService.History;

        // Load persisted settings
        LoadSettings();

        _pollTimer = new System.Timers.Timer(PollIntervalSeconds * 1000);
        _pollTimer.Elapsed += async (_, _) => await PollStatusAsync();
        _pollTimer.AutoReset = true;
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        GatewayHost = settings.GatewayHost;
        GatewayPort = settings.GatewayPort.ToString();
        PollIntervalSeconds = settings.PollIntervalSeconds;
        AutoStart = settings.AutoStartGateway;
        AutoRestart = settings.AutoRestartGateway;
        StartMinimized = settings.StartMinimized;
        StartWithWindows = settings.StartWithWindows;

        // Initialize editable settings
        SettingsGatewayHost = GatewayHost;
        SettingsGatewayPort = GatewayPort;
        SettingsPollIntervalSeconds = PollIntervalSeconds;
        SettingsAutoStart = AutoStart;
        SettingsAutoRestart = AutoRestart;
        SettingsStartMinimized = StartMinimized;
        SettingsStartWithWindows = StartWithWindows;
    }

    [RelayCommand]
    private async Task StartGatewayAsync()
    {
        if (IsRunning || IsStarting) return;

        try
        {
            GatewayStatus = GatewayStatus.Starting;
            IsStarting = true;
            UpdateStatusDisplay();
            AppendLog("Starting gateway...");
            Logger.Info("Start command issued");

            var success = await _gatewayService.StartAsync();
            if (success)
            {
                GatewayStatus = GatewayStatus.Running;
                IsRunning = true;
                IsStarting = false;
                AppendLog("Gateway started successfully");
                NotificationService.Show("OpenClaw Companion", "Gateway started successfully");
                StartPolling();
            }
            else
            {
                GatewayStatus = GatewayStatus.Error;
                IsStarting = false;
                AppendLog("ERROR: Gateway failed to start");
                NotificationService.Show("OpenClaw Companion", "Gateway failed to start");
                Logger.Error("Gateway start failed");
            }
        }
        catch (Exception ex)
        {
            GatewayStatus = GatewayStatus.Error;
            IsStarting = false;
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
        if (!IsRunning && !IsStarting) return;

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
                IsStarting = false;
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

    [RelayCommand]
    private void ToggleSettings()
    {
        if (!IsSettingsVisible)
        {
            // Copy current live settings into editable fields
            SettingsGatewayHost = GatewayHost;
            SettingsGatewayPort = GatewayPort;
            SettingsPollIntervalSeconds = PollIntervalSeconds;
            SettingsAutoStart = AutoStart;
            SettingsAutoRestart = AutoRestart;
            SettingsStartMinimized = StartMinimized;
            SettingsStartWithWindows = StartWithWindows;
        }
        IsSettingsVisible = !IsSettingsVisible;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // Validate port
        if (!int.TryParse(SettingsGatewayPort, out var port) || port < 1 || port > 65535)
        {
            AppendLog("ERROR: Invalid port number. Must be 1-65535.");
            return;
        }

        // Validate poll interval
        var interval = SettingsPollIntervalSeconds;
        if (interval < 5) interval = 5;
        if (interval > 300) interval = 300;

        // Update live settings
        GatewayHost = SettingsGatewayHost;
        GatewayPort = SettingsGatewayPort;
        PollIntervalSeconds = interval;
        AutoStart = SettingsAutoStart;
        AutoRestart = SettingsAutoRestart;
        StartMinimized = SettingsStartMinimized;

        // Handle StartWithWindows registry change
        if (SettingsStartWithWindows != StartWithWindows)
        {
            StartWithWindows = SettingsStartWithWindows;
            UpdateWindowsStartupRegistry(StartWithWindows);
        }

        // Persist
        var settings = new AppSettings
        {
            GatewayHost = GatewayHost,
            GatewayPort = port,
            PollIntervalSeconds = PollIntervalSeconds,
            AutoStartGateway = AutoStart,
            AutoRestartGateway = AutoRestart,
            StartMinimized = StartMinimized,
            StartWithWindows = StartWithWindows
        };
        _settingsService.Save(settings);

        // Apply to services
        _gatewayService.UpdateEndpoint(GatewayHost, port);

        // Restart timer if running
        if (_isPolling)
        {
            _pollTimer.Interval = PollIntervalSeconds * 1000;
        }

        AppendLog($"Settings saved — Host: {GatewayHost}, Port: {port}, Poll: {PollIntervalSeconds}s, AutoStart: {AutoStart}, AutoRestart: {AutoRestart}, StartMinimized: {StartMinimized}, StartWithWindows: {StartWithWindows}");
        Logger.Info($"Settings updated: {GatewayHost}:{port}, interval={PollIntervalSeconds}s, autoStart={AutoStart}, autoRestart={AutoRestart}, startMinimized={StartMinimized}, startWithWindows={StartWithWindows}");

        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void CancelSettings()
    {
        // Discard changes — editable fields are already separate, just hide
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void OpenConfigEditor()
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw",
                "openclaw.json");

            ConfigFilePath = configPath;

            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                try
                {
                    var doc = JsonDocument.Parse(json);
                    ConfigJsonText = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    ConfigJsonText = json;
                }
            }
            else
            {
                ConfigJsonText = $"Configuration file not found.{Environment.NewLine}{Environment.NewLine}Expected path:{Environment.NewLine}{configPath}";
            }

            IsConfigEditing = false;
            IsConfigEditorVisible = true;
            IsSettingsVisible = false;
        }
        catch (Exception ex)
        {
            ConfigJsonText = $"Error reading configuration file:{Environment.NewLine}{ex.Message}";
            IsConfigEditing = false;
            IsConfigEditorVisible = true;
        }
    }

    [RelayCommand]
    private void EnableConfigEdit()
    {
        IsConfigEditing = true;
    }

    [RelayCommand]
    private void SaveConfigEditor()
    {
        try
        {
            // Validate JSON
            JsonDocument.Parse(ConfigJsonText);
        }
        catch (JsonException ex)
        {
            AppendLog($"ERROR: Invalid JSON — {ex.Message}");
            NotificationService.Show("OpenClaw Companion", "Cannot save: Invalid JSON");
            return;
        }

        try
        {
            // Create backup
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = $"{ConfigFilePath}.backup.{timestamp}";

            if (File.Exists(ConfigFilePath))
            {
                File.Copy(ConfigFilePath, backupPath, overwrite: false);
                Logger.Info($"Config backup created: {backupPath}");
            }

            // Write new config
            File.WriteAllText(ConfigFilePath, ConfigJsonText);
            Logger.Info("Config saved successfully");

            AppendLog("Configuration saved successfully");
            NotificationService.Show("OpenClaw Companion", "Configuration saved");
            IsConfigEditing = false;
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: Failed to save config — {ex.Message}");
            NotificationService.Show("OpenClaw Companion", $"Config save failed: {ex.Message}");
            Logger.Error($"Config save failed: {ex}");
        }
    }

    [RelayCommand]
    private void CancelConfigEdit()
    {
        // Re-read the file to discard changes
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                try
                {
                    var doc = JsonDocument.Parse(json);
                    ConfigJsonText = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    ConfigJsonText = json;
                }
            }
        }
        catch { }
        IsConfigEditing = false;
    }

    [RelayCommand]
    private void CloseConfigEditor()
    {
        IsConfigEditorVisible = false;
        IsConfigEditing = false;
    }

    [RelayCommand]
    private void ShowQRCode()
    {
        try
        {
            if (!IsRunning)
            {
                AppendLog("QR Code: Gateway must be running to generate pairing URL");
                return;
            }

            var pairingUrl = $"http://{GatewayHost}:{GatewayPort}/pair";

            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(pairingUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);
            var pngBytes = qrCode.GetGraphic(20);

            using var ms = new MemoryStream(pngBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            ShowQRCodeRequested?.Invoke(bitmap, pairingUrl);
            Logger.Info($"QR code generated for {pairingUrl}");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: Failed to generate QR code — {ex.Message}");
            Logger.Error($"QR code generation failed: {ex}");
        }
    }

    [RelayCommand]
    private void ToggleCli()
    {
        IsCliVisible = !IsCliVisible;
        if (IsCliVisible)
        {
            IsDiagnosticsVisible = false;
            IsSettingsVisible = false;
            IsConfigEditorVisible = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteCliCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CliCommandInput) || IsCliExecuting) return;

        IsCliExecuting = true;
        var command = CliCommandInput.Trim();
        AppendLog($"CLI: > {command}");

        try
        {
            var result = await _cliService.ExecuteAsync(command);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"> {command}");
            if (!string.IsNullOrWhiteSpace(result.Output))
                sb.AppendLine(result.Output);
            if (!string.IsNullOrWhiteSpace(result.Error))
                sb.AppendLine($"[ERROR] {result.Error}");
            sb.AppendLine($"[exit: {result.ExitCode}]");
            sb.AppendLine();

            CliOutputText += sb.ToString();
            AppendLog($"CLI: exit code {result.ExitCode}");
        }
        catch (Exception ex)
        {
            CliOutputText += $"> {command}\n[ERROR] {ex.Message}\n\n";
            AppendLog($"ERROR: CLI execution failed — {ex.Message}");
        }

        CliCommandInput = string.Empty;
        IsCliExecuting = false;
        ResetCliHistoryIndex();
    }

    [RelayCommand]
    private void CliHistoryUp()
    {
        var history = _cliService.GetCommandHistory();
        if (history.Count == 0) return;

        if (_cliHistoryIndex == -1)
        {
            _cliSavedInput = CliCommandInput;
            _cliHistoryIndex = history.Count - 1;
        }
        else if (_cliHistoryIndex > 0)
        {
            _cliHistoryIndex--;
        }
        else
        {
            return;
        }

        CliCommandInput = history[_cliHistoryIndex];
    }

    [RelayCommand]
    private void CliHistoryDown()
    {
        var history = _cliService.GetCommandHistory();
        if (history.Count == 0 || _cliHistoryIndex == -1) return;

        if (_cliHistoryIndex < history.Count - 1)
        {
            _cliHistoryIndex++;
            CliCommandInput = history[_cliHistoryIndex];
        }
        else
        {
            _cliHistoryIndex = -1;
            CliCommandInput = _cliSavedInput;
        }
    }

    public void ResetCliHistoryIndex()
    {
        _cliHistoryIndex = -1;
        _cliSavedInput = string.Empty;
    }

    [RelayCommand]
    private void ClearCliOutput()
    {
        CliOutputText = string.Empty;
        _cliService.ClearHistory();
        ResetCliHistoryIndex();
    }

    [RelayCommand]
    private void ToggleDiagnostics()
    {
        IsDiagnosticsVisible = !IsDiagnosticsVisible;
        if (IsDiagnosticsVisible)
        {
            IsCliVisible = false;
            IsSettingsVisible = false;
            IsConfigEditorVisible = false;
        }
    }

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        IsDiagnosticsRunning = true;
        DiagnosticsResults.Clear();
        DiagnosticsStatusText = "Running diagnostics...";

        try
        {
            if (!int.TryParse(GatewayPort, out var port))
                port = 18789;

            var results = await _diagnosticsService.RunChecksAsync(GatewayHost, port);

            foreach (var check in results)
            {
                DiagnosticsResults.Add(check);
            }

            var passed = results.Count(r => r.Passed);
            var total = results.Count;
            DiagnosticsStatusText = $"{passed}/{total} checks passed";
            AppendLog($"Diagnostics complete: {passed}/{total} passed");
        }
        catch (Exception ex)
        {
            DiagnosticsStatusText = $"Diagnostics failed: {ex.Message}";
            AppendLog($"ERROR: Diagnostics failed — {ex.Message}");
        }

        IsDiagnosticsRunning = false;
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsUpdateCheckRunning = true;
        UpdateStatusText = "Checking for updates...";
        UpdateInfo = null;

        try
        {
            var info = await _updateService.CheckForUpdateAsync();
            UpdateInfo = info;

            if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                UpdateStatusText = $"Update check failed: {info.ErrorMessage}";
                AppendLog($"Update check failed: {info.ErrorMessage}");
            }
            else if (info.UpdateAvailable)
            {
                UpdateStatusText = $"Update available! {info.LocalVersion} → {info.LatestVersion}";
                AppendLog($"Update available: {info.LocalVersion} → {info.LatestVersion}");
                NotificationService.Show("OpenClaw Companion", $"Update available: {info.LatestVersion}");
            }
            else
            {
                UpdateStatusText = $"Up to date (v{info.LocalVersion})";
                AppendLog($"Up to date: v{info.LocalVersion}");
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Update check failed: {ex.Message}";
            AppendLog($"ERROR: Update check failed — {ex.Message}");
        }

        IsUpdateCheckRunning = false;
    }

    private void UpdateWindowsStartupRegistry(bool enabled)
    {
        const string runKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string appName = "OpenClawCompanion";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, true);
            if (key != null)
            {
                if (enabled)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(appName, exePath);
                        AppendLog("Start with Windows enabled");
                        Logger.Info("Registry Run key added for auto-start");
                    }
                }
                else
                {
                    if (key.GetValue(appName) != null)
                    {
                        key.DeleteValue(appName);
                        AppendLog("Start with Windows disabled");
                        Logger.Info("Registry Run key removed");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: Failed to update startup registry: {ex.Message}");
            Logger.Error($"Registry update failed: {ex}");
        }
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

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            // Track if auto-restart is needed
            bool needsAutoRestart = false;

            // Ensure we are on the UI thread for property updates
            await dispatcher.InvokeAsync(() =>
            {
                if (status == GatewayStatus.Running)
                {
                    _consecutiveDownCount = 0;
                    if (GatewayStatus != GatewayStatus.Running)
                    {
                        GatewayStatus = GatewayStatus.Running;
                        IsRunning = true;
                        IsStarting = false;
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
                        IsStarting = false;
                        ClearProcessInfo();
                        UpdateStatusDisplay();
                        StatusChanged?.Invoke(GatewayStatus);

                        if (previousStatus == GatewayStatus.Running)
                        {
                            AppendLog("WARNING: Gateway stopped unexpectedly");
                            NotificationService.Show("OpenClaw Companion", "Gateway stopped unexpectedly");
                            Logger.Warn("Gateway stopped unexpectedly (detected via poll)");

                            // Flag auto-restart if enabled
                            if (AutoRestart)
                            {
                                needsAutoRestart = true;
                            }
                        }
                    }
                    else if (_consecutiveDownCount < ConsecutiveDownThreshold)
                    {
                        Logger.Info($"Poll: Gateway check failed ({_consecutiveDownCount}/{ConsecutiveDownThreshold}) — waiting for confirmation");
                    }
                }
            });

            // Auto-restart outside of UI thread dispatcher
            if (needsAutoRestart)
            {
                _gatewayService.ResetRestartAttempts();
                var result = await _gatewayService.AutoRestartIfNeededAsync(AutoRestart);
                if (result.Attempted && result.Success)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        GatewayStatus = GatewayStatus.Running;
                        IsRunning = true;
                        UpdateProcessInfo();
                        UpdateStatusDisplay();
                        StatusChanged?.Invoke(GatewayStatus);
                        StartPolling();
                    });
                }
            }
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
