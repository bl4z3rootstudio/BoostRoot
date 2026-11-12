using System.Diagnostics;
using BoostRoot.App.Overlay;
using BoostRoot.Core.Clips;
using BoostRoot.Core.Hardware;
using BoostRoot.Platform.Windows;
using BoostRoot.ViewModels;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.App;

/// <summary>Replay buffer + manual recording + screenshots with ffmpeg (Desktop Duplication + GPU encoder)
/// and WASAPI audio. Everything runs below-normal priority and only while the game session is alive.</summary>
public sealed class ClipRecorder(ClipSettings settings, GpuVendor vendor) : IClipRecorder
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double Now() => _clock.Elapsed.TotalSeconds;
    private Process? _ffmpeg;
    private string? _dir;
    private SegmentBook? _book;
    private WasapiCapture? _sys, _mic;
    private CancellationTokenSource? _poll;
    private double? _t0;
    private int _csvLines;
    private bool _replay;
    private (double Start, double AudioStart)? _rec;
    private readonly Lock _lock = new();

    public static string Root(ClipSettings s) => s.Folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "BoostRoot");

    private static void Toast(string key, params object[] args) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ToastOverlay.Show(args.Length == 0 ? Loc.I[key] : Loc.I.Format(key, args)));

    private bool EnsurePipeline()
    {
        lock (_lock)
        {
            if (_ffmpeg is { HasExited: false }) return true;
            if (!Ffmpeg.Installed) { Toast("clips.toast.noFfmpeg"); return false; }
            _dir = Path.Combine(Path.GetTempPath(), "BoostRoot", "clips", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var keep = settings.ReplaySeconds + 10;
            _book = new SegmentBook(keep);
            _t0 = null; _csvLines = 0;
            _ffmpeg = Ffmpeg.Start(FfmpegArgs.Segments(vendor, settings.Height, settings.Fps, _dir), stdin: true);
            if (settings.Audio is "system" or "system+mic") _sys = new WasapiCapture(true, keep + 30, Now);
            if (settings.Audio is "system+mic") _mic = new WasapiCapture(false, keep + 30, Now);
            _poll = new CancellationTokenSource();
            _ = PollAsync(_poll.Token);
            return true;
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }
            var csv = Path.Combine(_dir!, "list.csv");
            if (!File.Exists(csv)) continue;
            string[] lines;
            try { using var f = new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); lines = new StreamReader(f).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries); }
            catch (IOException) { continue; }
            for (; _csvLines < lines.Length; _csvLines++)
            {
                if (SegmentBook.ParseCsv(lines[_csvLines].Trim()) is not { } s) continue;
                _t0 ??= Now() - s.End; // segment is flushed right after it ends
                _book!.Add(new Segment(Path.Combine(_dir!, s.File), _t0.Value + s.Start, _t0.Value + s.End));
            }
            foreach (var old in _book!.Prune(Now()))
                try { File.Delete(old.File); } catch (IOException) { }
        }
    }

    public void StartReplay(string game) { _replay = true; EnsurePipeline(); }

    public void SaveReplay(string game)
    {
        if (!EnsurePipeline() || _book?.Latest is not { } end) { Toast("clips.toast.notReady"); return; }
        var from = end - settings.ReplaySeconds;
        _ = SaveAsync(game, from, end, "clip", null);
    }

    public void ToggleRecording(string game)
    {
        if (_rec is { } r)
        {
            _rec = null;
            _book!.RecordingFrom = null;
            var lists = (_sys?.Ring?.Unbounded, _mic?.Ring?.Unbounded);
            if (_sys?.Ring is { } a) a.Unbounded = null;
            if (_mic?.Ring is { } b) b.Unbounded = null;
            _ = SaveAsync(game, r.Start, _book.Latest ?? Now(), "kayit", (r.AudioStart, lists.Item1, lists.Item2)).ContinueWith(_ => { if (!_replay) StopAll(); }, TaskScheduler.Default);
            return;
        }
        if (!EnsurePipeline()) return;
        var now = Now();
        _rec = (now, now);
        _book!.RecordingFrom = now - 2;
        if (_sys?.Ring is { } s) s.Unbounded = [];
        if (_mic?.Ring is { } m) m.Unbounded = [];
        Toast("clips.toast.recording");
    }

    private async Task SaveAsync(string game, double from, double to, string kind, (double Start, List<float>? Sys, List<float>? Mic)? recorded)
    {
        try
        {
            var segs = _book!.Range(from, to);
            if (segs.Count == 0) { Toast("clips.toast.notReady"); return; }
            var work = Path.Combine(_dir!, "save-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(work);
            var list = Path.Combine(work, "list.txt");
            await File.WriteAllTextAsync(list, SegmentBook.ConcatList(segs));
            var segStart = segs[0].Start;
            var wavs = new List<string>();
            void AddWav(string name, AudioRing ring, List<float>? rec)
            {
                float[] pcm;
                if (rec is not null && recorded is { } r)
                {
                    var pad = (int)Math.Max(0, (r.Start - segStart) * ring.Rate) * ring.Channels;
                    pcm = new float[pad + rec.Count];
                    rec.CopyTo(pcm, pad);
                }
                else pcm = ring.Read(segStart, segs[^1].End);
                var path = Path.Combine(work, name);
                File.WriteAllBytes(path, Wav.Encode(pcm, ring.Rate, ring.Channels));
                wavs.Add(path);
            }
            if (_sys?.Ring is { } sr) AddWav("sys.wav", sr, recorded?.Sys);
            if (_mic?.Ring is { } mr) AddWav("mic.wav", mr, recorded?.Mic);
            var output = ClipNames.Clip(Root(settings), game, DateTime.Now, kind, "mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var (code, _) = await Ffmpeg.RunAsync(FfmpegArgs.Join(list, Math.Max(0, from - segStart), to - Math.Max(from, segStart), wavs, output));
            Directory.Delete(work, true);
            Toast(code == 0 ? "clips.toast.saved" : "clips.toast.failed", Path.GetFileName(output));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Toast("clips.toast.failed", ex.Message);
        }
    }

    public void Screenshot(string game)
    {
        if (!Ffmpeg.Installed) { Toast("clips.toast.noFfmpeg"); return; }
        var output = ClipNames.Clip(Root(settings), game, DateTime.Now, "foto", "png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        _ = Task.Run(async () =>
        {
            var (code, _) = await Ffmpeg.RunAsync(FfmpegArgs.Screenshot(output));
            Toast(code == 0 ? "clips.toast.shot" : "clips.toast.failed", Path.GetFileName(output));
        });
    }

    public void StopAll()
    {
        lock (_lock)
        {
            _poll?.Cancel();
            try
            {
                if (_ffmpeg is { HasExited: false } p) { p.StandardInput.Write('q'); if (!p.WaitForExit(2000)) p.Kill(); }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
            _ffmpeg?.Dispose(); _ffmpeg = null;
            _sys?.Dispose(); _mic?.Dispose(); _sys = _mic = null;
            if (_dir is not null) try { Directory.Delete(_dir, true); } catch (IOException) { }
            _dir = null;
        }
    }
}
