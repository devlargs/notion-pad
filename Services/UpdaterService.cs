using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace NotionPad.Services;

public class UpdaterService
{
    private const string Owner = "devlargs";
    private const string Repo = "notion-pad";
    private const string AssetName = "NotionPad.exe";
    private const string ApiUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

    private readonly HttpClient _http;

    public UpdaterService(HttpClient http)
    {
        _http = http;
    }

    public async Task CheckAndApplyAsync()
    {
        if (!ShouldRun()) return;

        try
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
            var (latest, downloadUrl) = await FetchLatestAsync();
            if (latest is null || downloadUrl is null) return;
            if (latest <= current) return;

            var tempPath = Path.Combine(Path.GetTempPath(), $"NotionPad-update-{latest}.exe");
            await DownloadAsync(downloadUrl, tempPath);

            ScheduleSwapAndRestart(tempPath, latest);
        }
        catch
        {
            // Updates should never block the app — swallow network/JSON/IO failures silently.
        }
    }

    private static bool ShouldRun()
    {
#if DEBUG
        return false;
#else
        var current = Assembly.GetExecutingAssembly().GetName().Version;
        if (current is null || current.Major == 0) return false;
        var exe = Environment.ProcessPath ?? string.Empty;
        if (exe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
#endif
    }

    private async Task<(Version? latest, string? url)> FetchLatestAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        req.Headers.UserAgent.ParseAdd("NotionPad-Updater");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return (null, null);

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var tag = doc.RootElement.GetProperty("tag_name").GetString();
        if (string.IsNullOrEmpty(tag)) return (null, null);
        if (!Version.TryParse(tag.TrimStart('v'), out var version)) return (null, null);

        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName) continue;
            return (version, asset.GetProperty("browser_download_url").GetString());
        }
        return (version, null);
    }

    private async Task DownloadAsync(string url, string destPath)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst);
    }

    private static void ScheduleSwapAndRestart(string newExePath, Version version)
    {
        var currentExe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(currentExe)) return;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"NotionPad-updater-{Guid.NewGuid():N}.cmd");
        var script = $@"@echo off
timeout /t 2 /nobreak >NUL
:retry
move /Y ""{newExePath}"" ""{currentExe}""
if errorlevel 1 (timeout /t 1 /nobreak >NUL & goto retry)
start """" ""{currentExe}""
del ""%~f0""
";
        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            CreateNoWindow = true,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                $"Notion Pad will restart to apply update v{version}.",
                "Update available",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            app.Shutdown();
        });
    }
}
