using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using OpenClawCompanion.Models;

namespace OpenClawCompanion.Services;

public class CliService
{
    private readonly Queue<string> _commandHistory = new();
    private const int MaxHistory = 10;

    // Resolved via discovery (portable, no hardcoded paths). Prefers persisted last-good.
    private string _nodePath = "node";
    private string _openclawPath = string.Empty;

    public ObservableCollection<CliCommandOutput> History { get; } = new();

    public CliService()
    {
        // Mirror discovery from GatewayService for consistent portable CLI behavior
        try
        {
            var settings = new SettingsService().Load();
            var discoveredNode = DiagnosticsService.DiscoverNodeExecutable(settings.NodeExePath);
            var discoveredScript = DiagnosticsService.DiscoverOpenClawScript(settings.OpenClawMjsPath);
            _nodePath = discoveredNode ?? "node";
            _openclawPath = discoveredScript ?? string.Empty;
            Logger.Info($"CliService paths resolved — node: {_nodePath}, openclaw.mjs: {(string.IsNullOrEmpty(_openclawPath) ? "(not found)" : _openclawPath)}");
        }
        catch (Exception ex)
        {
            _nodePath = "node";
            _openclawPath = string.Empty;
            Logger.Warn($"Path discovery in CliService ctor failed (PATH fallback): {ex.Message}");
        }
    }

    public async Task<CliCommandOutput> ExecuteAsync(string command)
    {
        var output = new CliCommandOutput { Command = command };

        try
        {
            // Resolve effective paths each execution (re-discover if needed for robustness)
            string effectiveNode = string.IsNullOrEmpty(_nodePath) ? "node" : _nodePath;
            string effectiveScript = _openclawPath;
            if (string.IsNullOrEmpty(effectiveScript) || !File.Exists(effectiveScript))
            {
                effectiveScript = DiagnosticsService.DiscoverOpenClawScript() ?? string.Empty;
                if (!string.IsNullOrEmpty(effectiveScript)) _openclawPath = effectiveScript;
            }
            if (string.IsNullOrEmpty(effectiveScript))
            {
                output.Error = "OpenClaw script not found (discovery failed). Install via 'npm install -g openclaw'.";
                output.ExitCode = -1;
                AddToHistory(output);
                Logger.Error("CLI aborted: openclaw.mjs path unknown");
                return output;
            }
            if (effectiveNode != "node" && !File.Exists(effectiveNode))
            {
                effectiveNode = DiagnosticsService.DiscoverNodeExecutable() ?? "node";
                if (effectiveNode != "node") _nodePath = effectiveNode;
            }

            // Build arguments: gateway {command}
            var arguments = $"\"{effectiveScript}\" gateway {command}";

            var psi = new ProcessStartInfo(effectiveNode, arguments)
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                output.Error = "Failed to start process";
                output.ExitCode = -1;
                AddToHistory(output);
                return output;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            output.Output = stdout.Trim();
            output.Error = stderr.Trim();
            output.ExitCode = process.ExitCode;

            Logger.Info($"CLI command executed: {command} (exit: {output.ExitCode})");
        }
        catch (Exception ex)
        {
            output.Error = ex.Message;
            output.ExitCode = -1;
            Logger.Error($"CLI execution failed: {ex.Message}");
        }

        AddToHistory(output);
        return output;
    }

    private void AddToHistory(CliCommandOutput output)
    {
        _commandHistory.Enqueue(output.Command);
        while (_commandHistory.Count > MaxHistory)
            _commandHistory.Dequeue();

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            History.Add(output);
            while (History.Count > MaxHistory)
                History.RemoveAt(0);
        });
    }

    public void ClearHistory()
    {
        _commandHistory.Clear();
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            History.Clear();
        });
    }

    public IReadOnlyList<string> GetCommandHistory()
    {
        return _commandHistory.ToList().AsReadOnly();
    }
}
