using System.Globalization;
using System.Text;
using BoostRoot.Core.Hardware;

namespace BoostRoot.Core.Clips;

/// <summary>Builds every ffmpeg command line BoostRoot uses. Pure → unit tested.</summary>
public static class FfmpegArgs
{
    private static string I(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Hardware encoder for the GPU vendor (no CPU cost in game), libx264 as fallback.</summary>
    public static IReadOnlyList<string> Encoder(GpuVendor vendor, int fps) => vendor switch
    {
        GpuVendor.Nvidia => ["-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-cq", "23", "-b:v", "0", "-g", (fps * 2).ToString(CultureInfo.InvariantCulture)],
        GpuVendor.Amd => ["-c:v", "h264_amf", "-usage", "lowlatency", "-quality", "speed", "-rc", "cqp", "-qp_i", "22", "-qp_p", "24", "-g", (fps * 2).ToString(CultureInfo.InvariantCulture)],
        GpuVendor.Intel => ["-c:v", "h264_qsv", "-preset", "veryfast", "-global_quality", "23", "-g", (fps * 2).ToString(CultureInfo.InvariantCulture)],
        _ => ["-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-g", (fps * 2).ToString(CultureInfo.InvariantCulture)],
    };

    /// <summary>Desktop Duplication capture → 2 s MPEG-TS segments + CSV list (start/end of each).</summary>
    public static IReadOnlyList<string> Segments(GpuVendor vendor, int height, int fps, string dir) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-filter_complex", $"ddagrab=output_idx=0:framerate={fps}:draw_mouse=0,hwdownload,format=bgra,scale=-2:{height}:flags=fast_bilinear,format=nv12",
        .. Encoder(vendor, fps),
        "-f", "segment", "-segment_time", "2", "-reset_timestamps", "1",
        "-segment_list", Path.Combine(dir, "list.csv"), "-segment_list_type", "csv", "-segment_list_flags", "live",
        Path.Combine(dir, "seg%05d.ts"),
    ];

    public static IReadOnlyList<string> Screenshot(string output) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-filter_complex", "ddagrab=output_idx=0:framerate=10,hwdownload,format=bgra", "-frames:v", "1", output];

    /// <summary>Joins segments (stream copy) and muxes the captured audio tracks.</summary>
    public static IReadOnlyList<string> Join(string concatList, double trimStart, double duration, IReadOnlyList<string> wavs, string output)
    {
        var a = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", concatList };
        foreach (var w in wavs) a.AddRange(["-i", w]);
        a.AddRange(["-ss", I(trimStart), "-t", I(duration), "-map", "0:v"]);
        for (var i = 0; i < wavs.Count; i++) a.AddRange(["-map", $"{i + 1}:a"]);
        a.AddRange(["-c:v", "copy"]);
        if (wavs.Count > 0) a.AddRange(["-c:a", "aac", "-b:a", "160k"]);
        a.AddRange(["-movflags", "+faststart", output]);
        return a;
    }

    public static IReadOnlyList<string> Trim(string input, double start, double end, string output) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-ss", I(start), "-to", I(end), "-i", input, "-map", "0", "-c", "copy", "-movflags", "+faststart", output];

    /// <summary>9:16 centre crop for Shorts/TikTok/Reels.</summary>
    public static IReadOnlyList<string> Vertical(string input, string output) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-vf", "crop=ih*9/16:ih,scale=1080:1920", "-c:v", "libx264", "-preset", "veryfast", "-crf", "21", "-c:a", "copy", "-movflags", "+faststart", output];

    /// <summary>Re-encode to fit a size limit (Discord webhooks: 10 MB).</summary>
    public static IReadOnlyList<string> Shrink(string input, double seconds, long maxBytes, string output)
    {
        var kbps = Math.Max(300, (int)(maxBytes * 8 / 1000 / Math.Max(1, seconds) * 0.92) - 96);
        return ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-vf", "scale=-2:'min(720,ih)'", "-c:v", "libx264", "-preset", "veryfast",
            "-b:v", $"{kbps}k", "-maxrate", $"{kbps}k", "-bufsize", $"{kbps * 2}k", "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart", output];
    }

    public static IReadOnlyList<string> Thumbnail(string input, string output) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-ss", "1", "-i", input, "-frames:v", "1", "-vf", "scale=320:-2", output];

    /// <summary>Parses "Duration: 00:01:02.50" from ffmpeg's stderr.</summary>
    public static double? ParseDuration(string stderr)
    {
        var i = stderr.IndexOf("Duration: ", StringComparison.Ordinal);
        if (i < 0) return null;
        var t = stderr.AsSpan(i + 10, Math.Min(11, stderr.Length - i - 10));
        return TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts) ? ts.TotalSeconds : null;
    }
}

/// <summary>One finished segment; times are seconds on the session clock.</summary>
public sealed record Segment(string File, double Start, double End);

/// <summary>Tracks finished segments: what to delete (outside the replay window and not being recorded)
/// and which ones cover a requested time range.</summary>
public sealed class SegmentBook(double keepSeconds)
{
    private readonly List<Segment> _segs = [];
    private readonly Lock _lock = new();
    public double? RecordingFrom { get; set; }

    public void Add(Segment s) { lock (_lock) _segs.Add(s); }

    /// <summary>Segments that can be deleted now (removed from the book).</summary>
    public IReadOnlyList<Segment> Prune(double now)
    {
        lock (_lock)
        {
            var limit = Math.Min(now - keepSeconds - 4, RecordingFrom ?? double.MaxValue);
            var old = _segs.Where(s => s.End < limit).ToList();
            _segs.RemoveAll(old.Contains);
            return old;
        }
    }

    public IReadOnlyList<Segment> Range(double from, double to)
    {
        lock (_lock) return [.. _segs.Where(s => s.End > from && s.Start < to)];
    }

    public double? Latest { get { lock (_lock) return _segs.Count == 0 ? null : _segs[^1].End; } }

    /// <summary>Parses a line of ffmpeg's segment CSV ("seg00001.ts,2.000000,4.000000").</summary>
    public static (string File, double Start, double End)? ParseCsv(string line)
    {
        var p = line.Split(',');
        return p.Length >= 3 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? (p[0], a, b) : null;
    }

    public static string ConcatList(IEnumerable<Segment> segs) =>
        string.Concat(segs.Select(s => $"file '{s.File.Replace("'", "'\\''", StringComparison.Ordinal)}'\n"));
}

/// <summary>Interleaved float PCM with a session-clock start time; keeps the last N seconds.</summary>
public sealed class AudioRing(int rate, int channels, double keepSeconds)
{
    private readonly Lock _lock = new();
    private float[] _buf = new float[(int)(rate * channels * keepSeconds)];
    private long _written; // total samples ever written
    public int Rate { get; } = rate;
    public int Channels { get; } = channels;
    /// <summary>Session time of sample 0.</summary>
    public double StartTime { get; set; }
    /// <summary>When set, everything is also kept (recording).</summary>
    public List<float>? Unbounded { get; set; }

    public double Seconds => Interlocked.Read(ref _written) / (double)(Rate * Channels);

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (var s in samples) { _buf[_written % _buf.Length] = s; _written++; }
            Unbounded?.AddRange(samples);
        }
    }

    public void WriteSilence(int frames) { Span<float> z = stackalloc float[Math.Min(frames * Channels, 4096)]; var left = frames * Channels; while (left > 0) { var n = Math.Min(left, z.Length); Write(z[..n]); left -= n; } }

    /// <summary>Samples between two session times (silence where nothing was captured).</summary>
    public float[] Read(double from, double to)
    {
        var total = Math.Max(0, (int)((to - from) * Rate) * Channels);
        var res = new float[total];
        lock (_lock)
        {
            var first = (long)Math.Round((from - StartTime) * Rate) * Channels;
            var oldest = Math.Max(0, _written - _buf.Length);
            for (var i = 0; i < total; i++)
            {
                var idx = first + i;
                if (idx >= oldest && idx < _written) res[i] = _buf[idx % _buf.Length];
            }
        }
        return res;
    }
}

public static class Wav
{
    /// <summary>32-bit float WAV.</summary>
    public static byte[] Encode(ReadOnlySpan<float> samples, int rate, int channels)
    {
        var data = samples.Length * 4;
        var ms = new MemoryStream(44 + data);
        var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)3); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 4); w.Write((short)(channels * 4)); w.Write((short)32);
        w.Write("data"u8); w.Write(data);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}

public sealed record ClipFile(string Path, string Game, DateTime Created, long Bytes);

public static class ClipNames
{
    public static string Safe(string game) => string.Concat(game.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) || c is ':' ? '_' : c)).Trim();
    public static string Clip(string root, string game, DateTime at, string kind, string ext) =>
        System.IO.Path.Combine(root, Safe(game), $"{Safe(game)} {at:yyyy-MM-dd HH-mm-ss} {kind}.{ext}");
}
