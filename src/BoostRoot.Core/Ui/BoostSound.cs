namespace BoostRoot.Core.Ui;

/// <summary>Builds the short "whoosh-ping" played after a successful boost as an in-memory 16-bit mono WAV (no asset file).</summary>
public static class BoostSound
{
    public static byte[] Wav(int sampleRate = 22050, double seconds = 0.28, double volume = 0.18)
    {
        var n = (int)(sampleRate * seconds);
        using var ms = new MemoryStream(44 + n * 2);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var freq = 520 + 900 * t * t;                 // rising sweep
            phase += 2 * Math.PI * freq / sampleRate;
            var env = Math.Min(1, t * 25) * Math.Pow(1 - t, 2.2); // quick attack, smooth tail
            var s = (Math.Sin(phase) + 0.25 * Math.Sin(phase * 2)) * env * volume;
            w.Write((short)Math.Clamp(s * short.MaxValue, short.MinValue, short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>Laptop power state. Percent is null when unknown/no battery.</summary>
public sealed record BatteryState(bool HasBattery, bool OnAc, int? Percent)
{
    /// <summary>A laptop running on battery usually caps CPU/GPU clocks — the most common "why is my FPS low" cause.</summary>
    public bool HurtsFps => HasBattery && !OnAc;

    /// <summary>Maps GetSystemPowerStatus fields (ACLineStatus 0/1/255, BatteryFlag 128 = no battery, 255 = unknown).</summary>
    public static BatteryState FromWin32(byte acLine, byte batteryFlag, byte lifePercent)
    {
        var has = batteryFlag != 128 && batteryFlag != 255;
        return new(has, acLine != 0, has && lifePercent <= 100 ? lifePercent : null);
    }
}
