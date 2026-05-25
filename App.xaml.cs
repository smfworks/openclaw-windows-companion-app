using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using OpenClawCompanion.Services;
using OpenClawCompanion.ViewModels;

namespace OpenClawCompanion;

public partial class App : System.Windows.Application
{
    private const string MutexName = "OpenClawCompanion_SingleInstance";
    private static Mutex? _mutex;
    private TrayIconService? _trayIconService;
    private GatewayService? _gatewayService;
    private SettingsService? _settingsService;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance check
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Bring existing instance to front
            var hWnd = FindWindow(null, "OpenClaw Companion");
            if (hWnd != IntPtr.Zero)
            {
                if (IsIconic(hWnd))
                    ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
            Shutdown();
            return;
        }

        Logger.Info("=== OpenClaw Companion starting ===");

        // Initialize services
        _settingsService = new SettingsService();
        _gatewayService = new GatewayService();
        _viewModel = new MainViewModel(_gatewayService, _settingsService);
        _trayIconService = new TrayIconService(_gatewayService);

        // Apply persisted settings to gateway service
        var settings = _settingsService.Load();
        _gatewayService.UpdateEndpoint(settings.GatewayHost, settings.GatewayPort);

        // Create main window
        _mainWindow = new MainWindow
        {
            DataContext = _viewModel
        };

        // Wire up tray icon
        _trayIconService.Initialize(_mainWindow);
        _trayIconService.StartGatewayRequested += () => _mainWindow.Dispatcher.Invoke(async () => await _viewModel.StartGatewayCommand.ExecuteAsync(null));
        _trayIconService.StopGatewayRequested += () => _mainWindow.Dispatcher.Invoke(async () => await _viewModel.StopGatewayCommand.ExecuteAsync(null));
        _trayIconService.RestartGatewayRequested += () => _mainWindow.Dispatcher.Invoke(async () => await _viewModel.RestartGatewayCommand.ExecuteAsync(null));
        _trayIconService.ExitRequested += () => ShutdownApplication();
        _trayIconService.ShowRequested += () => _trayIconService.ShowWindow();

        // ViewModel -> TrayIcon status sync
        _viewModel.StatusChanged += status =>
        {
            _trayIconService.UpdateTooltip(status);
            _trayIconService.UpdateMenuState(status == Models.GatewayStatus.Running);
        };
        _viewModel.ShowWindowRequested += () => _trayIconService.ShowWindow();

        // Handle startup visibility based on StartMinimized setting
        if (settings.StartMinimized)
        {
            // Don't show the window — keep it hidden in tray
            Logger.Info("Starting minimized to tray");
        }
        else
        {
            // Show window normally on startup
            _mainWindow.Show();
            Logger.Info("Window shown on startup");
        }

        // Initialize polling / auto-start (async) — always runs regardless of window visibility
        _ = _viewModel.InitializeAsync();

        Logger.Info("Application startup complete");
    }

    private void ShutdownApplication()
    {
        Logger.Info("Application shutting down");

        // Allow the main window to close without being intercepted
        if (_mainWindow != null)
        {
            _mainWindow.AllowClose();
            _mainWindow.Close();
        }

        _viewModel?.StopPolling();
        _trayIconService?.Dispose();
        _mutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("=== OpenClaw Companion exiting ===");
        base.OnExit(e);
    }
}
