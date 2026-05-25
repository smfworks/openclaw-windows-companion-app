namespace OpenClawCompanion.Models;

public class DiagnosticsCheck
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Suggestion { get; set; }
}
