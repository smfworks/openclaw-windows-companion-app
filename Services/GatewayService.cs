using System.Diagnostics;
using System.Management;
using System.Net.Http;
using OpenClawCompanion.Models;

namespace OpenClawCompanion.Services;

public class GatewayService
{
    private readonly HttpClient _httpClient;

    private readonly string _nodePath = @"C:\Program Files\nodejs\node.exe";
    private readonly string _gatewayPath = @"C:\Users\Michael Gannotti\AppData\Roaming\npm\node_modules\openclaw\openclaw.mjs";

    private string _gatewayHost = "localhost";
    private int _gatewayPort = 18789;

    private int? _gatewayPid;
    private DateTime? _startTime;

    // CPU tracking fields
    private DateTime _lastCpuSampleTime = DateTime.MinValue;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;

    public int? GatewayPid => _gatewayPid;
    public DateTime? StartTime => _startTime;

    public GatewayService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        UpdateEndpoint("localhost", 18789);
    }

    public void UpdateEndpoint(string host, int port)
    {
        _gatewayHost = host;
        _gatewayPort = port;
        _httpClient.BaseAddress = new Uri($"http://{_gatewayHost}:{_gatewayPort}");
        Logger.Info($"Gateway endpoint updated to {_gatewayHost}:{_gatewayPort}");
    }

    public async Task<GatewayStatus> GetStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/status");
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

        var psi = new ProcessStartInfo(_nodePath, $"\"{_gatewayPath}\" gateway run --force --port {_gatewayPort}")
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
        // Reset CPU tracking on new process
        _lastCpuSampleTime = DateTime.MinValue;
        _lastTotalProcessorTime = TimeSpan.Zero;
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

        // Check if gateway is actually running before attempting stop
        if (await GetStatusAsync() != GatewayStatus.Running)
        {
            Logger.Warn("Gateway is not running, nothing to stop");
            _gatewayPid = null;
            _startTime = null;
            _lastCpuSampleTime = DateTime.MinValue;
            _lastTotalProcessorTime = TimeSpan.Zero;
            return true;
        }

        await FindAndKillOpenClawProcessAsync();

        // Wait up to 5 seconds for confirmation
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            if (await GetStatusAsync() == GatewayStatus.Stopped)
            {
                _gatewayPid = null;
                _startTime = null;
                _lastCpuSampleTime = DateTime.MinValue;
                _lastTotalProcessorTime = TimeSpan.Zero;
                Logger.Info("Gateway stopped successfully");
                return true;
            }
        }

        Logger.Warn("Gateway stop timed out — process may still be running");
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

            // CPU calculation using TotalProcessorTime delta
            double cpuPercent = 0;
            try
            {
                process.Refresh();
                var now = DateTime.Now;
                var currentTotalProcessorTime = process.TotalProcessorTime;

                if (_lastCpuSampleTime != DateTime.MinValue && _lastTotalProcessorTime != TimeSpan.Zero)
                {
                    var cpuUsedMs = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
                    var elapsedMs = (now - _lastCpuSampleTime).TotalMilliseconds;
                    if (elapsedMs > 0)
                    {
                        cpuPercent = Math.Round((cpuUsedMs / elapsedMs) * 100.0 / Environment.ProcessorCount, 1);
                    }
                }

                _lastCpuSampleTime = now;
                _lastTotalProcessorTime = currentTotalProcessorTime;
            }
            catch
            {
                cpuPercent = 0;
            }

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
            _lastCpuSampleTime = DateTime.MinValue;
            _lastTotalProcessorTime = TimeSpan.Zero;
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
