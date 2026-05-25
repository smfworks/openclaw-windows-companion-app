using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClawCompanion.Models;

namespace OpenClawCompanion.Services;

public class UpdateService
{
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<UpdateInfo> CheckForUpdateAsync()
    {
        var info = new UpdateInfo();

        try
        {
            // Get local version
            info.LocalVersion = await GetLocalVersionAsync();

            // Get latest version from npm registry
            var registryUrl = "https://registry.npmjs.org/openclaw/latest";
            var response = await _httpClient.GetAsync(registryUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("version", out var versionElement))
            {
                info.LatestVersion = versionElement.GetString() ?? "Unknown";
            }

            info.UpdateAvailable = IsVersionGreater(info.LatestVersion, info.LocalVersion);
            Logger.Info($"Update check: local={info.LocalVersion}, latest={info.LatestVersion}, updateAvailable={info.UpdateAvailable}");
        }
        catch (HttpRequestException ex)
        {
            info.ErrorMessage = $"Network error: {ex.Message}";
            Logger.Error($"Update check failed: {ex.Message}");
        }
        catch (JsonException ex)
        {
            info.ErrorMessage = $"Invalid response from registry: {ex.Message}";
            Logger.Error($"Update check failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            info.ErrorMessage = $"Unexpected error: {ex.Message}";
            Logger.Error($"Update check failed: {ex.Message}");
        }

        return info;
    }

    private async Task<string> GetLocalVersionAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("npm", "list -g openclaw --depth=0")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null) return "Unknown";

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Parse version from output like: +-- openclaw@2.5.1
            var match = Regex.Match(output, @"openclaw@([\d\.]+)");
            if (match.Success)
                return match.Groups[1].Value;

            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private bool IsVersionGreater(string? latest, string? local)
    {
        if (string.IsNullOrEmpty(latest) || string.IsNullOrEmpty(local)) return false;
        if (latest == "Unknown" || local == "Unknown") return false;

        try
        {
            var latestParts = latest.Split('.').Select(int.Parse).ToArray();
            var localParts = local.Split('.').Select(int.Parse).ToArray();

            for (int i = 0; i < Math.Max(latestParts.Length, localParts.Length); i++)
            {
                var latestPart = i < latestParts.Length ? latestParts[i] : 0;
                var localPart = i < localParts.Length ? localParts[i] : 0;

                if (latestPart > localPart) return true;
                if (latestPart < localPart) return false;
            }

            return false; // Equal
        }
        catch
        {
            return false;
        }
    }
}
