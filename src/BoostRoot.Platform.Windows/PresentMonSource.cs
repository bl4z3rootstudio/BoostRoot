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

    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");

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
