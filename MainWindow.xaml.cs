using System.Windows;
using System.Windows.Input;
using OpenClawCompanion.Services;
using OpenClawCompanion.ViewModels;

namespace OpenClawCompanion;

public partial class MainWindow : Window
{
    private bool _allowClose = false;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            Logger.Info("Window hidden to tray (close prevented)");
        }
    }
}
