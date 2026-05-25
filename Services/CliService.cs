using System.Collections.ObjectModel;
using System.Diagnostics;
using OpenClawCompanion.Models;

namespace OpenClawCompanion.Services;

public class CliService
{
    private readonly Queue<string> _commandHistory = new();
    private const int MaxHistory = 10;

    private readonly string _nodePath = @"C:\Program Files\nodejs\node.exe";
    private readonly string _openclawPath = @"C:\Users\Michael Gannotti\AppData\Roaming\npm\node_modules\openclaw\openclaw.mjs";

    public ObservableCollection<CliCommandOutput> History { get; } = new();

    public async Task<CliCommandOutput> ExecuteAsync(string command)
    {
        var output = new CliCommandOutput { Command = command };

        try
        {
            // Build arguments: gateway {command}
            var arguments = $"\"{_openclawPath}\" gateway {command}";

            var psi = new ProcessStartInfo(_nodePath, arguments)
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
