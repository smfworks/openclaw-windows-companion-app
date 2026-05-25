namespace OpenClawCompanion.Models;

public class UpdateInfo
{
    public string LocalVersion { get; set; } = "Unknown";
    public string LatestVersion { get; set; } = "Unknown";
    public bool UpdateAvailable { get; set; }
    public string UpdateCommand { get; set; } = "npm install -g openclaw@latest";
    public string? ErrorMessage { get; set; }
}
