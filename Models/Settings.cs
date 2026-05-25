namespace OpenClawCompanion.Models;

public class AppSettings
{
    public string GatewayHost { get; set; } = "localhost";
    public int GatewayPort { get; set; } = 18789;
    public int PollIntervalSeconds { get; set; } = 10;
    public bool AutoStartGateway { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
}
