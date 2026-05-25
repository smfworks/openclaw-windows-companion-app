namespace OpenClawCompanion.Models;

public class CliCommandOutput
{
    public string Command { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool Success => ExitCode == 0 && string.IsNullOrEmpty(Error);
}
