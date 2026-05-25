using System.IO;
using System.Text.Json;
using OpenClawCompanion.Models;

namespace OpenClawCompanion.Services;

public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenClawCompanion");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    // Clamp poll interval to valid range
                    if (settings.PollIntervalSeconds < 5)
                        settings.PollIntervalSeconds = 5;
                    if (settings.PollIntervalSeconds > 300)
                        settings.PollIntervalSeconds = 300;
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

            // Clamp poll interval to valid range before saving
            if (settings.PollIntervalSeconds < 5)
                settings.PollIntervalSeconds = 5;
            if (settings.PollIntervalSeconds > 300)
                settings.PollIntervalSeconds = 300;

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
            Logger.Info("Settings saved successfully");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save settings: {ex.Message}");
        }
    }
}
