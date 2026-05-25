namespace OpenClawCompanion.Models;

public class ProcessInfo
{
    public int Pid { get; set; }
    public double MemoryMB { get; set; }
    public string Uptime { get; set; } = "00:00:00";
    public double CpuPercent { get; set; }
}
