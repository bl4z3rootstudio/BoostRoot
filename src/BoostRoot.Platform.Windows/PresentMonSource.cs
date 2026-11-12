using System.Diagnostics;
using BoostRoot.Core.Fps;

namespace BoostRoot.Platform.Windows;

/// <summary>Streams frame times of one process from PresentMon (Intel, MIT licensed), bundled in tools\.
/// PresentMon reads ETW events only — nothing is injected into the game, so anti-cheat safe.</summary>
public sealed class PresentMonSource : IFrameSource
{
    private readonly Process? _proc;
    public event Action<double>? Frame;
    public string? Error { get; }

    /// <summary>Bundled by the installer; portable builds download it on demand to LocalAppData.</summary>
    public static string ExePath => File.Exists(Bundled) ? Bundled : Downloaded;
    private static string Bundled => Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");
    public static string Downloaded { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoostRoot", "tools", "PresentMon.exe");
    public static bool Installed => File.Exists(Bundled) || File.Exists(Downloaded);

    /// <summary>Downloads the latest PresentMon console exe from GitHub; verifies the asset's SHA-256 digest when GitHub provides one.</summary>
    public static async Task InstallAsync(HttpClient http, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/GameTechDev/PresentMon/releases/latest");
        req.Headers.UserAgent.ParseAdd("BoostRoot");
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("PresentMon-", StringComparison.Ordinal) || !name.EndsWith("-x64.exe", StringComparison.Ordinal)) continue;
            var bytes = await http.GetByteArrayAsync(a.GetProperty("browser_download_url").GetString(), ct).ConfigureAwait(false);
            if (a.TryGetProperty("digest", out var dg) && dg.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.Ordinal)
                && !string.Equals(digest[7..], Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PresentMon checksum mismatch");
            Directory.CreateDirectory(Path.GetDirectoryName(Downloaded)!);
            await File.WriteAllBytesAsync(Downloaded, bytes, ct).ConfigureAwait(false);
            return;
        }
        throw new FileNotFoundException("PresentMon asset not found");
    }

    public PresentMonSource(IReadOnlyList<string> processNames)
    {
        if (!File.Exists(ExePath)) { Error = "presentmon-missing"; return; }
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in new[] { "--output_stdout", "--no_console_stats", "--stop_existing_session", "--session_name", "BoostRoot" }) psi.ArgumentList.Add(a);
        foreach (var n in processNames) { psi.ArgumentList.Add("--process_name"); psi.ArgumentList.Add(n); }
        try
        {
            _proc = Process.Start(psi);
            if (_proc is null) { Error = "presentmon-start"; return; }
            var csv = new PresentMonCsv();
            _proc.OutputDataReceived += (_, e) => { if (e.Data is { } l && csv.Parse(l) is { } ms) Frame?.Invoke(ms); };
            _proc.ErrorDataReceived += (_, _) => { };
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
        }
        catch (System.ComponentModel.Win32Exception) { Error = "presentmon-start"; }
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(); } catch (InvalidOperationException) { }
        _proc?.Dispose();
    }
}
