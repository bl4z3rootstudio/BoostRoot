using BoostRoot.Core.Clips;
using BoostRoot.Core.Hardware;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class ClipTests
{
    [Fact]
    public void Encoder_MatchesVendor()
    {
        Assert.Contains("h264_amf", FfmpegArgs.Encoder(GpuVendor.Amd, 60));
        Assert.Contains("h264_nvenc", FfmpegArgs.Encoder(GpuVendor.Nvidia, 60));
        Assert.Contains("libx264", FfmpegArgs.Encoder(GpuVendor.Unknown, 60));
        var seg = FfmpegArgs.Segments(GpuVendor.Amd, 1080, 60, "C:/t");
        Assert.Contains(seg, a => a.Contains("ddagrab", StringComparison.Ordinal) && a.Contains("scale=-2:1080", StringComparison.Ordinal));
    }

    [Fact]
    public void Join_MapsAudioTracks_AndShrinkFitsLimit()
    {
        var j = FfmpegArgs.Join("l.txt", 1.5, 30, ["s.wav", "m.wav"], "o.mp4");
        Assert.Equal(["-map", "0:v", "-map", "1:a", "-map", "2:a"], j.SkipWhile(a => a != "-map").Take(6));
        Assert.Contains("1.5", j);
        Assert.DoesNotContain("-c:a", FfmpegArgs.Join("l.txt", 0, 30, [], "o.mp4"));
        var s = FfmpegArgs.Shrink("i.mp4", 30, 10_000_000, "o.mp4");
        var kbps = int.Parse(s[s.ToList().IndexOf("-b:v") + 1].TrimEnd('k'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(kbps * 1000 / 8 * 30 + 96 * 1000 / 8 * 30, 0, 10_000_000);
    }

    [Fact]
    public void Duration_Parsed() => Assert.Equal(62.5, FfmpegArgs.ParseDuration("Input #0\n  Duration: 00:01:02.50, start: 0.0, bitrate: 1 kb/s"));

    [Fact]
    public void SegmentBook_PrunesKeepsRecordingAndRanges()
    {
        var b = new SegmentBook(10);
        for (var i = 0; i < 20; i++) b.Add(new Segment($"s{i}", i * 2, i * 2 + 2));
        Assert.Equal((("seg00001.ts", 2.0, 4.0)), SegmentBook.ParseCsv("seg00001.ts,2.000000,4.000000"));
        b.RecordingFrom = 6;
        Assert.Equal(2, b.Prune(40).Count); // only segments ending before the recording start
        b.RecordingFrom = null;
        var pruned = b.Prune(40);
        Assert.All(pruned, s => Assert.True(s.End < 26));
        Assert.Equal(["s15", "s16"], b.Range(30.5, 33.5).Select(s => s.File));
        Assert.Equal("file 'a'\\''b'\n", SegmentBook.ConcatList([new Segment("a'b", 0, 1)]));
    }

    [Fact]
    public void AudioRing_ReadsByTime_WithSilenceOutside()
    {
        var r = new AudioRing(10, 1, 5) { StartTime = 100 };
        r.Write(Enumerable.Range(1, 30).Select(i => (float)i).ToArray());
        var x = r.Read(101, 102);
        Assert.Equal(Enumerable.Range(11, 10).Select(i => (float)i), x);
        Assert.All(r.Read(90, 91), v => Assert.Equal(0, v));
        r.Write(Enumerable.Repeat(9f, 40).ToArray()); // oldest samples overwritten
        Assert.All(r.Read(100, 101), v => Assert.Equal(0, v));
    }

    [Fact]
    public void Wav_HasHeader()
    {
        var w = Wav.Encode([0.5f, -0.5f], 48000, 2);
        Assert.Equal(44 + 8, w.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(w, 0, 4));
        Assert.Equal(3, BitConverter.ToInt16(w, 20));
    }

    [Fact]
    public void Names_AreSafePerGameFolders() =>
        Assert.Equal(Path.Combine("C:/V", "Tom Clancy_s R6", "Tom Clancy_s R6 2026-11-12 21-00-00 clip.mp4"), ClipNames.Clip("C:/V", "Tom Clancy's R6".Replace('\'', '_'), new DateTime(2026, 11, 12, 21, 0, 0), "clip", "mp4"));

    [Fact]
    public async Task ViewModel_InstallsListsTrimsSends()
    {
        var svc = ViewModelTests.Services();
        var settings = AppSettings.Default with { Clips = new ClipSettings(DiscordWebhook: "https://discord.com/api/webhooks/1/x") };
        var vm = new ClipsViewModel(svc, () => settings);
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.True(vm.FfmpegInstalled);
        vm.Refresh();
        Assert.Equal(2, vm.Clips.Count);
        Assert.Equal(3, vm.Games.Count);
        vm.GameFilter = "CS2";
        Assert.Single(vm.Clips);
        vm.GameFilter = ClipsViewModel.AllGames;
        vm.Selected = vm.Clips.First(c => !c.IsImage);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Equal(30, vm.TrimEnd);
        vm.TrimStart = 5;
        await vm.TrimCommand.ExecuteAsync(null);
        Assert.Equal([("C:/Clips/VALORANT/a clip.mp4", 5.0, 30.0)], svc.Trims);
        await vm.SendDiscordCommand.ExecuteAsync(null);
        Assert.Single(svc.Sent);
    }
}
