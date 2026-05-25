using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (DataContext is MainViewModel vm)
        {
            vm.ShowQRCodeRequested += OnShowQRCodeRequested;
        }
    }

    private void OnShowQRCodeRequested(BitmapImage qrImage, string pairingUrl)
    {
        var qrWindow = new QRCodeWindow(qrImage, pairingUrl)
        {
            Owner = this
        };
        qrWindow.ShowDialog();
    }
}
