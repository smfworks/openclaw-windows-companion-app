using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OpenClawCompanion;

public partial class QRCodeWindow : Window
{
    public QRCodeWindow(BitmapImage qrImage, string pairingUrl)
    {
        InitializeComponent();
        QRImage.Source = qrImage;
        UrlTextBlock.Text = pairingUrl;
        DataContext = this;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnBorderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnCopyUrlClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(UrlTextBlock.Text);
        CopyFeedback.Text = "Copied!";
        CopyFeedback.Visibility = Visibility.Visible;

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) =>
        {
            CopyFeedback.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }
}
