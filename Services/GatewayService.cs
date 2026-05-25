using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using OpenClawCompanion.Models;

namespace OpenClawCompanion.Services;

public class GatewayService
{
    private readonly HttpClient _httpClient;

    // Resolved at construction via discovery (prefers persisted last-known-good from settings).
    // Falls back to bare "node" (PATH) or empty for script (will re-discover on use).
    private string _nodePath = "node";
    private string _gatewayPath = string.Empty;

    private string _gatewayHost = "localhost";
    private int _gatewayPort = 18789;

    private int? _gatewayPid;
    private DateTime? _startTime;

    // Live process reference (preferred over PID for lifetime management and cleanup)
    private Process? _gatewayProcess;

    // Auto-restart tracking (consecutive attempts since last manual user action)
    private int _restartAttempts;
    private const int MaxRestartAttempts = 5;

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

        // Portable path discovery (critical fix for hardcoded paths).
        // Reuses DiagnosticsService helpers (common locations + where/PATH).
        // Prefers any last-known-good persisted in settings.json.
        try
        {
            var settings = new SettingsService().Load();
            var discoveredNode = DiagnosticsService.DiscoverNodeExecutable(settings.NodeExePath);
            var discoveredScript = DiagnosticsService.DiscoverOpenClawScript(settings.OpenClawMjsPath);
            _nodePath = discoveredNode ?? "node"; // bare "node" lets ProcessStartInfo resolve via PATH
            _gatewayPath = discoveredScript ?? string.Empty;
            Logger.Info($"GatewayService paths resolved — node: {_nodePath}, openclaw.mjs: {(string.IsNullOrEmpty(_gatewayPath) ? "(not found)" : _gatewayPath)}");
        }
        catch (Exception ex)
        {
            _nodePath = "node";
            _gatewayPath = string.Empty;
            Logger.Warn($"Path discovery in GatewayService ctor failed (will use PATH fallback): {ex.Message}");
        }
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

        // Guard / re-discover paths if necessary (handles case where discovery at ctor found nothing)
        string effectiveNode = string.IsNullOrEmpty(_nodePath) ? "node" : _nodePath;
        string effectiveGateway = _gatewayPath;
        if (string.IsNullOrEmpty(effectiveGateway) || !File.Exists(effectiveGateway))
        {
            effectiveGateway = DiagnosticsService.DiscoverOpenClawScript() ?? string.Empty;
            if (!string.IsNullOrEmpty(effectiveGateway))
                _gatewayPath = effectiveGateway; // cache for future
        }
        if (string.IsNullOrEmpty(effectiveGateway))
        {
            Logger.Error("Cannot start gateway: openclaw.mjs not found via discovery or persisted path. Run 'npm install -g openclaw' or configure path.");
            return false;
        }
        if (!string.IsNullOrEmpty(effectiveNode) && effectiveNode != "node" && !File.Exists(effectiveNode))
        {
            effectiveNode = DiagnosticsService.DiscoverNodeExecutable() ?? "node";
            if (effectiveNode != "node") _nodePath = effectiveNode;
        }

        var psi = new ProcessStartInfo(effectiveNode, $"\"{effectiveGateway}\" gateway run --force --port {_gatewayPort}")
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

        // Track live process + pid + timestamps (improved lifetime management)
        _gatewayProcess = process;
        _gatewayPid = process.Id;
        _startTime = DateTime.Now;
        // Reset CPU tracking on new process
        _lastCpuSampleTime = DateTime.MinValue;
        _lastTotalProcessorTime = TimeSpan.Zero;
        Logger.Info($"Node process started with PID {process.Id}");

        // Proper long-running stream handling via events (replaces dangerous fire-and-forget ReadToEnd).
        // Events are non-blocking and will not hang waiting for process exit.
        process.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                Logger.Info($"[gateway stdout] {args.Data.Trim()}");
        };
        process.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                Logger.Error($"[gateway stderr] {args.Data.Trim()}");
        };
        process.EnableRaisingEvents = true;
        process.Exited += OnGatewayProcessExited;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

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

    private void OnGatewayProcessExited(object? sender, EventArgs e)
    {
        // Cleanup tracked refs when OS reports natural or external exit.
        // Poller will detect status change and may trigger auto-restart if enabled.
        Logger.Warn($"Tracked gateway process exited (PID {_gatewayPid})");
        CleanupTrackedProcess();
    }

    private void CleanupTrackedProcess()
    {
        _gatewayPid = null;
        _startTime = null;
        _lastCpuSampleTime = DateTime.MinValue;
        _lastTotalProcessorTime = TimeSpan.Zero;

        if (_gatewayProcess != null)
        {
            try { _gatewayProcess.Exited -= OnGatewayProcessExited; } catch { }
            try { _gatewayProcess.Dispose(); } catch { }
            _gatewayProcess = null;
        }
    }

    public async Task<bool> StopAsync()
    {
        Logger.Info("Stopping gateway...");

        // Check if gateway is actually running before attempting stop
        if (await GetStatusAsync() != GatewayStatus.Running)
        {
            Logger.Warn("Gateway is not running, nothing to stop");
            CleanupTrackedProcess();
            return true;
        }

        await FindAndKillOpenClawProcessAsync();

        // Wait up to 5 seconds for confirmation
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            if (await GetStatusAsync() == GatewayStatus.Stopped)
            {
                CleanupTrackedProcess();
                Logger.Info("Gateway stopped successfully");
                return true;
            }
        }

        Logger.Warn("Gateway stop timed out — process may still be running");
        return false;
    }

    private async Task FindAndKillOpenClawProcessAsync()
    {
        // Prefer live tracked process reference (most reliable + avoids broad kill)
        if (_gatewayProcess != null && !_gatewayProcess.HasExited)
        {
            try
            {
                Logger.Info($"Killing tracked gateway process (PID {_gatewayProcess.Id})");
                _gatewayProcess.Kill(entireProcessTree: true);
                await _gatewayProcess.WaitForExitAsync();
                CleanupTrackedProcess();
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to kill tracked process ref: {ex.Message}; falling back to PID/taskkill");
            }
        }

        // Next best: tracked PID only (no broad LIKE search)
        if (_gatewayPid.HasValue)
        {
            try
            {
                Logger.Info($"Killing gateway by tracked PID {_gatewayPid.Value}");
                var killPsi = new ProcessStartInfo("taskkill", $"/PID {_gatewayPid.Value} /F /T")
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };
                var killProc = Process.Start(killPsi);
                if (killProc != null)
                    await killProc.WaitForExitAsync();
                CleanupTrackedProcess();
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Tracked PID kill failed: {ex.Message}");
            }
        }

        // Last resort: broad search (original behavior, but now only if no tracked info)
        // NOTE: This can affect other node processes with "openclaw" in cmdline.
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='node.exe' AND CommandLine LIKE '%openclaw%'");

            foreach (ManagementObject obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                Logger.Info($"Killing node.exe PID {pid} (openclaw gateway via fallback search)");

                var killPsi = new ProcessStartInfo("taskkill", $"/PID {pid} /F /T")
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };
                var killProc = Process.Start(killPsi);
                if (killProc != null)
                    await killProc.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to find/kill openclaw process: {ex.Message}");
        }
        finally
        {
            // Ensure cleanup even on fallback path
            if (_gatewayPid != null || _gatewayProcess != null)
                CleanupTrackedProcess();
        }
    }

    public ProcessInfo? GetProcessInfo()
    {
        // Prefer live process reference when available (more robust than PID alone)
        var process = _gatewayProcess;
        if (process == null && _gatewayPid.HasValue)
        {
            try { process = Process.GetProcessById(_gatewayPid.Value); } catch { }
        }
        if (process == null || _gatewayPid == null) return null;

        try
        {
            if (process.Id != _gatewayPid.Value)
            {
                // stale ref? refresh
                process = Process.GetProcessById(_gatewayPid.Value);
            }

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
            CleanupTrackedProcess();
            return null;
        }
    }

    public async Task<bool> RestartAsync()
    {
        Logger.Info("Restarting gateway...");
        await StopAsync();
        return await StartAsync();
    }

    /// <summary>
    /// Resets consecutive auto-restart attempt counter.
    /// Called on manual user Start/Stop actions so that crash recovery quota is replenished.
    /// </summary>
    public void ResetRestartAttempts()
    {
        if (_restartAttempts > 0)
            Logger.Info($"Auto-restart attempt counter reset (was {_restartAttempts})");
        _restartAttempts = 0;
    }

    /// <summary>
    /// Performs an auto-restart with backoff and attempt limiting if enabled.
    /// Called by the poller when unexpected gateway death is detected.
    /// Returns (Attempted, Success) to match call-site expectations in MainViewModel.
    /// </summary>
    public async Task<(bool Attempted, bool Success)> AutoRestartIfNeededAsync(bool enabled)
    {
        if (!enabled)
            return (false, false);

        if (_restartAttempts >= MaxRestartAttempts)
        {
            Logger.Warn($"Auto-restart skipped: reached max consecutive attempts ({MaxRestartAttempts}). Manual intervention required.");
            return (false, false);
        }

        _restartAttempts++;
        // Exponential backoff with cap: 1s, 2s, 4s, 4s...
        int delayMs = Math.Min(1000 * (1 << (_restartAttempts - 1)), 4000);
        Logger.Info($"Auto-restart attempt {_restartAttempts}/{MaxRestartAttempts} — waiting {delayMs}ms before StartAsync");
        await Task.Delay(delayMs);

        bool started = await StartAsync();
        if (started)
        {
            Logger.Info("Auto-restart succeeded via StartAsync");
            return (true, true);
        }

        Logger.Warn($"Auto-restart attempt {_restartAttempts} failed");
        return (true, false);
    }
}
