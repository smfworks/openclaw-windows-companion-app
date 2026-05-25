using Windows.UI.Notifications;

namespace OpenClawCompanion.Services;

public static class NotificationService
{
    public static void Show(string title, string message)
    {
        try
        {
            var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textNodes = template.GetElementsByTagName("text");
            textNodes[0].AppendChild(template.CreateTextNode(title));
            textNodes[1].AppendChild(template.CreateTextNode(message));

            var notifier = ToastNotificationManager.CreateToastNotifier("OpenClawCompanion");
            var notification = new ToastNotification(template);
            notifier.Show(notification);

            Logger.Info($"Notification: {title} - {message}");
        }
        catch (Exception ex)
        {
            // Fallback: Toast API may fail if app identity not set
            Logger.Warn($"Toast notification failed: {ex.Message}");
        }
    }
}
