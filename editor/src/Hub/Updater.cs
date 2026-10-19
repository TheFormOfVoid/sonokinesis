using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

namespace Sonokinesis.Editor.Hub;

/// <summary>A published release: its version, notes and downloads.</summary>
public sealed class ReleaseInfo
{
    public string Version = "", Tag = "", Notes = "", PageUrl = "";
    public Dictionary<string, (string Url, long Size)> Assets = new(StringComparer.OrdinalIgnoreCase);

    public (string Url, long Size)? Asset(string prefix, string ext) =>
        Assets.Where(a => a.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && a.Key.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
              .Select(a => ((string, long)?)a.Value).FirstOrDefault();
}

/// <summary>
/// Looks for new releases on GitHub and installs them: the setup of the new version is downloaded and
/// run, and it starts the app again when it is done. Also fetches the optional AI tool.
/// </summary>
public static class Updater
{
    public static string ReleasesPage => $"https://github.com/{Core.BuildInfo.Repo}/releases";
    private static readonly HttpClient http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Sonokinesis/" + Core.BuildInfo.Version);
        return c;
    }

    public static string AppDir => Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";

    /// <summary>True when the app was put in by its setup (which can then update it in place).</summary>
    public static bool InstalledBySetup => !OS.HasFeature("editor") && File.Exists(Path.Combine(AppDir, "unins000.exe"));

    /// <summary>The latest release. Throws with a readable message when it cannot be reached.</summary>
    public static async Task<ReleaseInfo> Latest()
    {
        using var res = await http.GetAsync($"https://api.github.com/repos/{Core.BuildInfo.Repo}/releases/latest");
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) throw new Exception("no public release was found");
        if (!res.IsSuccessStatusCode) throw new Exception("GitHub answered " + (int)res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var r = doc.RootElement;
        var info = new ReleaseInfo
        {
            Tag = r.GetProperty("tag_name").GetString() ?? "",
            Notes = r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "",
            PageUrl = r.TryGetProperty("html_url", out var u) ? u.GetString() : ReleasesPage,
        };
        info.Version = info.Tag.TrimStart('v', 'V');
        foreach (var a in r.GetProperty("assets").EnumerateArray())
            info.Assets[a.GetProperty("name").GetString()] = (a.GetProperty("browser_download_url").GetString(), a.GetProperty("size").GetInt64());
        return info;
    }

    /// <summary>Whether version a is newer than b ("0.3.1" &gt; "0.3.0").</summary>
    public static bool IsNewer(string a, string b)
    {
        static Version P(string s)
        {
            var core = new string((s ?? "").TrimStart('v', 'V').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            return System.Version.TryParse(core.Contains('.') ? core : core + ".0", out var v) ? v : new Version(0, 0);
        }
        return P(a) > P(b);
    }

    /// <summary>Downloads a file, reporting progress from 0 to 1.</summary>
    public static async Task Download(string url, string file, Action<double> progress, CancellationToken cancel = default)
    {
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        res.EnsureSuccessStatusCode();
        long total = res.Content.Headers.ContentLength ?? -1, done = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        var part = file + ".part";
        await using (var src = await res.Content.ReadAsStreamAsync(cancel))
        await using (var dst = File.Create(part))
        {
            var buffer = new byte[1 << 16];
            int n;
            while ((n = await src.ReadAsync(buffer, cancel)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), cancel);
                done += n;
                if (total > 0) progress?.Invoke((double)done / total);
            }
        }
        if (File.Exists(file)) File.Delete(file);
        File.Move(part, file);
    }

    /// <summary>Downloads the new setup and starts it; the caller quits the app right after.</summary>
    public static async Task DownloadAndRunSetup(ReleaseInfo release, Action<double> progress)
    {
        var asset = release.Asset("Sonokinesis-Setup", ".exe") ?? throw new Exception("this release has no setup to download");
        var file = Path.Combine(Path.GetTempPath(), "Sonokinesis-update", $"Sonokinesis-Setup-{release.Version}.exe");
        await Download(asset.Url, file, progress);
        // Silent update: it closes this app, installs over it and starts it again.
        Process.Start(new ProcessStartInfo(file, $"/SILENT /SUPPRESSMSGBOXES /NOCANCEL /CLOSEAPPLICATIONS /DIR=\"{AppDir}\"") { UseShellExecute = true });
    }

    // ---------------- AI tool ----------------

    /// <summary>The command-line tool and MCP server, kept in tools/ inside the app folder.</summary>
    public static string ToolExe
    {
        get
        {
            var inApp = Path.Combine(AppDir, "tools", "sonokinesis.exe");
            if (File.Exists(inApp)) return inApp;
            // Running from source: the tool built next to the editor.
            try
            {
                var dev = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "dist", "Sonokinesis-" + Core.BuildInfo.Version, "tools", "sonokinesis.exe"));
                if (File.Exists(dev)) return dev;
            }
            catch { }
            return null;
        }
    }

    /// <summary>The downloaded tool's version ("" if unknown).</summary>
    public static string ToolVersion(string exe)
    {
        try { return (FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "").Split('+')[0]; } catch { return ""; }
    }

    /// <summary>Downloads the AI tool of this version and unpacks it into tools/.</summary>
    public static async Task DownloadTool(Action<double> progress)
    {
        var release = await Latest();
        var asset = release.Asset("Sonokinesis-AI-Tool", ".zip") ?? throw new Exception("the latest release has no AI tool to download");
        var zip = Path.Combine(Path.GetTempPath(), "Sonokinesis-update", "ai-tool.zip");
        await Download(asset.Url, zip, progress);
        var dest = Path.Combine(AppDir, "tools");
        Directory.CreateDirectory(dest);
        using (var z = ZipFile.OpenRead(zip))
            foreach (var e in z.Entries.Where(e => e.Name.Length > 0))
                e.ExtractToFile(Path.Combine(dest, e.Name), true);
        File.Delete(zip);
    }

    /// <summary>The settings an AI app needs to use the tool as an MCP server.</summary>
    public static string McpConfig(string exe) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["mcpServers"] = new Dictionary<string, object> { ["sonokinesis"] = new { command = exe, args = new[] { "mcp" } } },
        }, new JsonSerializerOptions { WriteIndented = true });
}
