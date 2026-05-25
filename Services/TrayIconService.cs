using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using OpenClawCompanion.Models;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;

namespace OpenClawCompanion.Services;

public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;
    private readonly GatewayService _gatewayService;
    private ToolStripMenuItem? _showHideItem;
    private ToolStripMenuItem? _startItem;
    private ToolStripMenuItem? _stopItem;
    private ToolStripMenuItem? _restartItem;

    public event Action? ShowRequested;
    public event Action? StartGatewayRequested;
    public event Action? StopGatewayRequested;
    public event Action? RestartGatewayRequested;
    public event Action? ExitRequested;

    public TrayIconService(GatewayService gatewayService)
    {
        _gatewayService = gatewayService;
    }

    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;

        var contextMenu = new ContextMenuStrip();

        _showHideItem = new ToolStripMenuItem("Show", null, (_, _) => ToggleVisibility());
        _startItem = new ToolStripMenuItem("Start Gateway", null, (_, _) => StartGatewayRequested?.Invoke());
        _stopItem = new ToolStripMenuItem("Stop Gateway", null, (_, _) => StopGatewayRequested?.Invoke());
        _restartItem = new ToolStripMenuItem("Restart Gateway", null, (_, _) => RestartGatewayRequested?.Invoke());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke());

        contextMenu.Items.Add(_showHideItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_startItem);
        contextMenu.Items.Add(_stopItem);
        contextMenu.Items.Add(_restartItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        var icon = CreateTrayIcon();

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "OpenClaw Companion - Status: Stopped",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.MouseClick += OnTrayIconClick;

        Logger.Info("Tray icon initialized");
    }

    public void UpdateTooltip(GatewayStatus status)
    {
        if (_notifyIcon == null) return;
        _notifyIcon.Text = $"OpenClaw Companion - Status: {status}";
    }

    public void UpdateMenuState(bool isRunning)
    {
        if (_startItem != null) _startItem.Enabled = !isRunning;
        if (_stopItem != null) _stopItem.Enabled = isRunning;
        if (_restartItem != null) _restartItem.Enabled = isRunning;
    }

    private void OnTrayIconClick(object? sender, MouseEventArgs e)
    {
        if (_notifyIcon == null) return;
        if (e.Button == MouseButtons.Left)
        {
            ToggleVisibility();
        }
    }

    private void ToggleVisibility()
    {
        if (_mainWindow == null) return;

        if (_mainWindow.Visibility == Visibility.Visible)
        {
            _mainWindow.Hide();
            if (_showHideItem != null) _showHideItem.Text = "Show";
        }
        else
        {
            ShowRequested?.Invoke();
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            if (_showHideItem != null) _showHideItem.Text = "Hide";
        }
    }

    public void ShowWindow()
    {
        if (_mainWindow == null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        if (_showHideItem != null) _showHideItem.Text = "Hide";
    }

    private Icon CreateTrayIcon()
    {
        var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(0, 200, 180));
        g.FillEllipse(brush, 1, 1, 14, 14);

        using var pen = new Pen(Color.FromArgb(0, 160, 140), 1f);
        g.DrawEllipse(pen, 1, 1, 14, 14);

        using var font = new Font(new FontFamily("Segoe UI"), 7, System.Drawing.FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString("O", font, textBrush, 3, 1);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        Logger.Info("Tray icon disposed");
    }
}
