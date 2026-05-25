using System.IO;
using System.Text;

namespace OpenClawCompanion.Services;

public static class Logger
{
    private static readonly string LogPath =
        Path.Combine(Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), "openclaw-companion.log");

    private static readonly object _lock = new();

    public static void Info(string message) => Log("INFO", message);
    public static void Error(string message) => Log("ERROR", message);
    public static void Warn(string message) => Log("WARN", message);

    private static void Log(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8); }
            catch { /* best effort */ }
        }
    }

    public static string GetLogPath() => LogPath;
}
