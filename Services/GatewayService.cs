using System.Diagnostics;
using System.Management;
using System.Net.Http;
using OpenClawCompanion.Models;

namespace OpenClawCompanion.Services;

public class GatewayService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
        BaseAddress = new Uri("http://localhost:18789")
    };

    private readonly string _nodePath = @"C:\Program Files\nodejs\node.exe";
    private readonly string _gatewayPath = @"C:\Users\Michael Gannotti\AppData\Roaming\npm\node_modules\openclaw\openclaw.mjs";

    private int? _gatewayPid;
    private DateTime? _startTime;

    public int? GatewayPid => _gatewayPid;
    public DateTime? StartTime => _startTime;

    public async Task<GatewayStatus> GetStatusAsync()
    {
        try
        {
            var response = await HttpClient.GetAsync("/status");
            return response.IsSuccessStatusCode ? GatewayStatus.Running : GatewayStatus.Stopped;
        }
        catch
        {
            return GatewayStatus.Stopped;
        }
    }

    public async Task<bool> StartAsync()
    {
        Logger.Info("Starting gateway...");

        if (await GetStatusAsync() == GatewayStatus.Running)
        {
            Logger.Warn("Gateway already running");
            return false;
        }

        var psi = new ProcessStartInfo(_nodePath, $"\"{_gatewayPath}\" gateway start --port 18789")
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = Process.Start(psi);
        if (process == null)
        {
            Logger.Error("Failed to start node process");
            return false;
        }

        _gatewayPid = process.Id;
        _startTime = DateTime.Now;
        Logger.Info($"Node process started with PID {process.Id}");

        // Fire-and-forget read stdout/stderr to avoid buffer deadlock
        _ = Task.Run(() =>
        {
            var stdout = process.StandardOutput.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stdout))
                Logger.Info($"[gateway stdout] {stdout.Trim()}");
        });
        _ = Task.Run(() =>
        {
            var stderr = process.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stderr))
                Logger.Error($"[gateway stderr] {stderr.Trim()}");
        });

        // Wait up to 10 seconds for gateway to be ready
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            if (await GetStatusAsync() == GatewayStatus.Running)
            {
                Logger.Info("Gateway started successfully");
                return true;
            }
        }

        Logger.Warn("Gateway start timed out waiting for status endpoint");
        return false;
    }

    public async Task<bool> StopAsync()
    {
        Logger.Info("Stopping gateway...");

        await FindAndKillOpenClawProcessAsync();

        // Wait up to 5 seconds for confirmation
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            if (await GetStatusAsync() == GatewayStatus.Stopped)
            {
                _gatewayPid = null;
                _startTime = null;
                Logger.Info("Gateway stopped successfully");
                return true;
            }
        }

        Logger.Warn("Gateway stop timed out");
        return false;
    }

    private async Task FindAndKillOpenClawProcessAsync()
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='node.exe' AND CommandLine LIKE '%openclaw%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                Logger.Info($"Killing node.exe PID {pid} (openclaw gateway)");

                var killPsi = new ProcessStartInfo("taskkill", $"/PID {pid} /F")
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };
                var killProc = Process.Start(killPsi);
                if (killProc != null)
                    await killProc.WaitForExitAsync();
            }
            searcher.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to find/kill openclaw process: {ex.Message}");
        }
    }

    public ProcessInfo? GetProcessInfo()
    {
        if (_gatewayPid == null) return null;

        try
        {
            var process = Process.GetProcessById(_gatewayPid.Value);
            var uptime = _startTime.HasValue
                ? (DateTime.Now - _startTime.Value).ToString(@"hh\:mm\:ss")
                : "???";

            double memoryMB = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), 1);
            double cpuPercent = 0;

            try
            {
                // Approximate CPU via total processor time delta
                using var cpuCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName, true);
                cpuCounter.NextValue();
                Thread.Sleep(100);
                cpuPercent = Math.Round(cpuCounter.NextValue() / Environment.ProcessorCount, 1);
                cpuCounter.Dispose();
            }
            catch { /* CPU counter may not be available */ }

            return new ProcessInfo
            {
                Pid = process.Id,
                MemoryMB = memoryMB,
                Uptime = uptime,
                CpuPercent = cpuPercent
            };
        }
        catch
        {
            _gatewayPid = null;
            return null;
        }
    }

    public async Task<bool> RestartAsync()
    {
        Logger.Info("Restarting gateway...");
        await StopAsync();
        return await StartAsync();
    }
}
