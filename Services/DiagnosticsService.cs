using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClawCompanion.Models;

namespace OpenClawCompanion.Services;

public class DiagnosticsService
{
    private readonly HttpClient _httpClient;

    public DiagnosticsService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Discovers full path to node.exe.
    /// Prefers a persisted last-known-good path (if still valid on disk).
    /// Checks common install locations, then falls back to "where node.exe" in PATH.
    /// Returns null if not found (caller should fall back to bare "node" for PATH resolution).
    /// </summary>
    public static string? DiscoverNodeExecutable(string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
            return preferred;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var commonPaths = new[]
        {
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe",
            Path.Combine(userProfile, @"AppData\Local\Programs\nodejs\node.exe"),
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Fallback to PATH lookup via where.exe (same pattern as diagnostics checks)
        try
        {
            var psi = new ProcessStartInfo("where", "node.exe")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var paths = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in paths)
                    {
                        var trimmed = p.Trim();
                        if (File.Exists(trimmed))
                            return trimmed;
                    }
                }
            }
        }
        catch
        {
            // Ignore discovery errors; fall through to null
        }

        return null;
    }

    /// <summary>
    /// Discovers full path to openclaw.mjs gateway script.
    /// Prefers persisted last-known-good if valid.
    /// Checks common global npm locations (matching DiagnosticsService.CheckOpenClawAccessibleAsync),
    /// then falls back to "where openclaw.mjs".
    /// </summary>
    public static string? DiscoverOpenClawScript(string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
            return preferred;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var possiblePaths = new[]
        {
            Path.Combine(userProfile, @"AppData\Roaming\npm\node_modules\openclaw\openclaw.mjs"),
            Path.Combine(userProfile, @"AppData\Local\npm\node_modules\openclaw\openclaw.mjs"),
            @"C:\Program Files\nodejs\node_modules\openclaw\openclaw.mjs",
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Fallback to PATH via where (supports cases where npm global bin exposes it)
        try
        {
            var psi = new ProcessStartInfo("where", "openclaw.mjs")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var paths = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in paths)
                    {
                        var trimmed = p.Trim();
                        if (File.Exists(trimmed))
                            return trimmed;
                    }
                }
            }
        }
        catch
        {
            // best effort
        }

        return null;
    }

    public async Task<List<DiagnosticsCheck>> RunChecksAsync(string gatewayHost, int gatewayPort)
    {
        var checks = new List<DiagnosticsCheck>();

        // 1. Node.js installed
        checks.Add(await CheckNodeJsInstalledAsync());

        // 2. Node.js in PATH
        checks.Add(await CheckNodeInPathAsync());

        // 3. OpenClaw accessible
        checks.Add(await CheckOpenClawAccessibleAsync());

        // 4. Port availability
        checks.Add(await CheckPortAvailabilityAsync(gatewayHost, gatewayPort));

        // 5. Config valid
        checks.Add(await CheckConfigValidAsync());

        return checks;
    }

    private async Task<DiagnosticsCheck> CheckNodeJsInstalledAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null)
                return new DiagnosticsCheck
                {
                    Name = "Node.js Installed",
                    Passed = false,
                    Message = "Could not run node --version",
                    Suggestion = "Install Node.js from https://nodejs.org/"
                };

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return new DiagnosticsCheck
                {
                    Name = "Node.js Installed",
                    Passed = true,
                    Message = $"Node.js version: {output.Trim()}"
                };
            }

            return new DiagnosticsCheck
            {
                Name = "Node.js Installed",
                Passed = false,
                Message = "Node.js is not installed or not working",
                Suggestion = "Install Node.js from https://nodejs.org/"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsCheck
            {
                Name = "Node.js Installed",
                Passed = false,
                Message = $"Error checking Node.js: {ex.Message}",
                Suggestion = "Install Node.js from https://nodejs.org/"
            };
        }
    }

    private async Task<DiagnosticsCheck> CheckNodeInPathAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("where", "node.exe")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null)
                return new DiagnosticsCheck
                {
                    Name = "Node.js in PATH",
                    Passed = false,
                    Message = "Could not run where node.exe",
                    Suggestion = "Add Node.js to your system PATH environment variable"
                };

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var paths = output.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                return new DiagnosticsCheck
                {
                    Name = "Node.js in PATH",
                    Passed = true,
                    Message = $"Found at: {paths.FirstOrDefault()?.Trim() ?? "unknown"}"
                };
            }

            return new DiagnosticsCheck
            {
                Name = "Node.js in PATH",
                Passed = false,
                Message = "node.exe not found in PATH",
                Suggestion = "Add Node.js to your system PATH environment variable"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsCheck
            {
                Name = "Node.js in PATH",
                Passed = false,
                Message = $"Error checking PATH: {ex.Message}",
                Suggestion = "Add Node.js to your system PATH environment variable"
            };
        }
    }

    private async Task<DiagnosticsCheck> CheckOpenClawAccessibleAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("where", "openclaw.mjs")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                // Fallback: check common locations
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var possiblePaths = new[]
                {
                    Path.Combine(homeDir, @"AppData\Roaming\npm\node_modules\openclaw\openclaw.mjs"),
                    Path.Combine(homeDir, @"AppData\Local\npm\node_modules\openclaw\openclaw.mjs"),
                    @"C:\Program Files\nodejs\node_modules\openclaw\openclaw.mjs",
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return new DiagnosticsCheck
                        {
                            Name = "OpenClaw Accessible",
                            Passed = true,
                            Message = $"Found at: {path}"
                        };
                    }
                }

                return new DiagnosticsCheck
                {
                    Name = "OpenClaw Accessible",
                    Passed = false,
                    Message = "openclaw.mjs not found in PATH or common locations",
                    Suggestion = "Run: npm install -g openclaw"
                };
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var paths = output.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                return new DiagnosticsCheck
                {
                    Name = "OpenClaw Accessible",
                    Passed = true,
                    Message = $"Found at: {paths.FirstOrDefault()?.Trim() ?? "unknown"}"
                };
            }

            return new DiagnosticsCheck
            {
                Name = "OpenClaw Accessible",
                Passed = false,
                Message = "openclaw.mjs not found in PATH",
                Suggestion = "Run: npm install -g openclaw"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsCheck
            {
                Name = "OpenClaw Accessible",
                Passed = false,
                Message = $"Error checking OpenClaw: {ex.Message}",
                Suggestion = "Run: npm install -g openclaw"
            };
        }
    }

    private async Task<DiagnosticsCheck> CheckPortAvailabilityAsync(string host, int port)
    {
        try
        {
            // Check if the port is already in use
            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpListeners = ipProperties.GetActiveTcpListeners();
            var tcpConnections = ipProperties.GetActiveTcpConnections();

            var inUse = tcpListeners.Any(l => l.Port == port) ||
                        tcpConnections.Any(c => c.LocalEndPoint.Port == port);

            if (inUse)
            {
                // Try to see if it's our gateway
                try
                {
                    var response = await _httpClient.GetAsync($"http://{host}:{port}/status");
                    if (response.IsSuccessStatusCode)
                    {
                        return new DiagnosticsCheck
                        {
                            Name = $"Port {port} Available",
                            Passed = true,
                            Message = $"Port {port} is in use by OpenClaw gateway"
                        };
                    }
                }
                catch { }

                return new DiagnosticsCheck
                {
                    Name = $"Port {port} Available",
                    Passed = false,
                    Message = $"Port {port} is already in use by another application",
                    Suggestion = $"Change the gateway port in Settings or close the other application using port {port}"
                };
            }

            return new DiagnosticsCheck
            {
                Name = $"Port {port} Available",
                Passed = true,
                Message = $"Port {port} is available"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsCheck
            {
                Name = $"Port {port} Available",
                Passed = false,
                Message = $"Error checking port: {ex.Message}",
                Suggestion = "Try using a different port number"
            };
        }
    }

    private async Task<DiagnosticsCheck> CheckConfigValidAsync()
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw",
                "openclaw.json");

            if (!File.Exists(configPath))
            {
                return new DiagnosticsCheck
                {
                    Name = "Configuration Valid",
                    Passed = true,
                    Message = "No config file found — gateway will use defaults"
                };
            }

            var json = await File.ReadAllTextAsync(configPath);
            using var doc = JsonDocument.Parse(json);

            return new DiagnosticsCheck
            {
                Name = "Configuration Valid",
                Passed = true,
                Message = $"Valid JSON configuration at: {configPath}"
            };
        }
        catch (JsonException ex)
        {
            return new DiagnosticsCheck
            {
                Name = "Configuration Valid",
                Passed = false,
                Message = $"Invalid JSON: {ex.Message}",
                Suggestion = "Fix the JSON syntax in your openclaw.json file"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticsCheck
            {
                Name = "Configuration Valid",
                Passed = false,
                Message = $"Error reading config: {ex.Message}",
                Suggestion = "Check that your .openclaw\\openclaw.json file is accessible"
            };
        }
    }
}
