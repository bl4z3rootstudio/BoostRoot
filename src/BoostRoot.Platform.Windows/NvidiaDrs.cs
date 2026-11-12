using System.Runtime.InteropServices;
using BoostRoot.Core.Gpu;

namespace BoostRoot.Platform.Windows;

/// <summary>NVIDIA Driver Settings (DRS) via nvapi64.dll — the same per-app profiles NVIDIA Control Panel edits.
/// Creates a "BoostRoot - game.exe" profile when the game has none, otherwise edits the existing one and
/// remembers previous values for undo.</summary>
public sealed unsafe class NvidiaDrs : IGpuDriverProfiles
{
    private const int StrLen = 2048; // NvAPI_UnicodeString = NvU16[2048]
    private const int ProfileSize = 4 + StrLen * 2 + 16;          // NVDRS_PROFILE_V1
    private const int AppSize = 8 + StrLen * 2 * 3;                // NVDRS_APPLICATION_V1
    private const int SettingSize = 0x3020;                         // NVDRS_SETTING_V1
    private static uint Ver(int size, int v) => (uint)size | ((uint)v << 16);

    private readonly delegate* unmanaged<uint, nint> _qi;
    public bool Supported { get; }

    public NvidiaDrs()
    {
        if (!NativeLibrary.TryLoad("nvapi64.dll", out var lib) || !NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out var qi)) return;
        _qi = (delegate* unmanaged<uint, nint>)qi;
        var init = (delegate* unmanaged<int>)_qi(0x0150E828);
        Supported = init != null && init() == 0;
    }

    private delegate* unmanaged<nint*, int> CreateSession => (delegate* unmanaged<nint*, int>)_qi(0x0694D52E);
    private delegate* unmanaged<nint, int> LoadSettings => (delegate* unmanaged<nint, int>)_qi(0x375DBD6B);
    private delegate* unmanaged<nint, int> SaveSettings => (delegate* unmanaged<nint, int>)_qi(0xFCBC7E14);
    private delegate* unmanaged<nint, int> DestroySession => (delegate* unmanaged<nint, int>)_qi(0xDAD9CFF8);
    private delegate* unmanaged<nint, char*, nint*, byte*, int> FindApp => (delegate* unmanaged<nint, char*, nint*, byte*, int>)_qi(0xEEE566B2);
    private delegate* unmanaged<nint, byte*, nint*, int> CreateProfile => (delegate* unmanaged<nint, byte*, nint*, int>)_qi(0xCC176068);
    private delegate* unmanaged<nint, char*, nint*, int> FindProfile => (delegate* unmanaged<nint, char*, nint*, int>)_qi(0x7E4A9A0B);
    private delegate* unmanaged<nint, nint, byte*, int> CreateApp => (delegate* unmanaged<nint, nint, byte*, int>)_qi(0x4347A9DE);
    private delegate* unmanaged<nint, nint, byte*, int> SetSetting => (delegate* unmanaged<nint, nint, byte*, int>)_qi(0x577DD202);
    private delegate* unmanaged<nint, nint, uint, byte*, int> GetSetting => (delegate* unmanaged<nint, nint, uint, byte*, int>)_qi(0x73BF8338);
    private delegate* unmanaged<nint, nint, int> DeleteProfile => (delegate* unmanaged<nint, nint, int>)_qi(0x17093206);
    private delegate* unmanaged<nint, nint, uint, int> RestoreDefault => (delegate* unmanaged<nint, nint, uint, int>)_qi(0x53F0381E);

    private static void PutStr(byte* dst, string s)
    {
        var span = new Span<char>(dst, StrLen);
        span.Clear();
        s.AsSpan(0, Math.Min(s.Length, StrLen - 1)).CopyTo(span);
    }

    private T WithSession<T>(Func<nint, T> work)
    {
        nint s;
        if (CreateSession(&s) != 0) throw new InvalidOperationException("NVAPI session failed");
        try
        {
            if (LoadSettings(s) != 0) throw new InvalidOperationException("NVAPI load failed");
            return work(s);
        }
        finally { DestroySession(s); }
    }

    private nint FindAppProfile(nint s, string exe)
    {
        var name = stackalloc char[StrLen];
        new Span<char>(name, StrLen).Clear();
        exe.AsSpan().CopyTo(new Span<char>(name, StrLen));
        var app = stackalloc byte[AppSize];
        new Span<byte>(app, AppSize).Clear();
        *(uint*)app = Ver(AppSize, 1);
        nint profile;
        return FindApp(s, name, &profile, app) == 0 ? profile : 0;
    }

    private uint? Read(nint s, nint profile, uint id)
    {
        var buf = stackalloc byte[SettingSize];
        new Span<byte>(buf, SettingSize).Clear();
        *(uint*)buf = Ver(SettingSize, 1);
        if (GetSetting(s, profile, id, buf) != 0) return null;
        var location = *(uint*)(buf + 4 + StrLen * 2 + 8);
        return location == 0 ? *(uint*)(buf + 4 + StrLen * 2 + 20 + 4100) : null; // only values stored in this profile
    }

    private int Write(nint s, nint profile, uint id, uint value)
    {
        var buf = stackalloc byte[SettingSize];
        new Span<byte>(buf, SettingSize).Clear();
        *(uint*)buf = Ver(SettingSize, 1);
        *(uint*)(buf + 4 + StrLen * 2) = id;          // settingId
        *(uint*)(buf + 4 + StrLen * 2 + 4) = 0;       // NVDRS_DWORD_TYPE
        *(uint*)(buf + 4 + StrLen * 2 + 20 + 4100) = value; // currentValue.u32
        return SetSetting(s, profile, buf);
    }

    public (int Changes, string Backup) Apply(string exeName, IReadOnlyDictionary<uint, uint> settings) => WithSession(s =>
    {
        var profile = FindAppProfile(s, exeName);
        var created = false;
        if (profile == 0)
        {
            var p = stackalloc byte[ProfileSize];
            new Span<byte>(p, ProfileSize).Clear();
            *(uint*)p = Ver(ProfileSize, 1);
            PutStr(p + 4, "BoostRoot - " + exeName);
            *(uint*)(p + 4 + StrLen * 2) = 1; // gpuSupport.geforce
            if (CreateProfile(s, p, &profile) != 0) return (0, "created=0");
            var app = stackalloc byte[AppSize];
            new Span<byte>(app, AppSize).Clear();
            *(uint*)app = Ver(AppSize, 1);
            PutStr(app + 8, exeName);
            if (CreateApp(s, profile, app) != 0) { DeleteProfile(s, profile); return (0, "created=0"); }
            created = true;
        }
        var previous = new Dictionary<uint, uint?>();
        var changes = 0;
        foreach (var (id, value) in settings)
        {
            var old = Read(s, profile, id);
            if (old == value) continue;
            if (Write(s, profile, id, value) == 0) { previous[id] = old; changes++; }
        }
        if (changes > 0 || created) SaveSettings(s);
        return (changes, NvidiaSettings.Encode(created, previous));
    });

    public void Revert(string exeName, string backup) => WithSession(s =>
    {
        var profile = FindAppProfile(s, exeName);
        if (profile == 0) return 0;
        var (created, previous) = NvidiaSettings.Decode(backup);
        if (created) DeleteProfile(s, profile);
        else
            foreach (var (id, old) in previous)
                if (old is { } v) Write(s, profile, id, v); else RestoreDefault(s, profile, id);
        SaveSettings(s);
        return 0;
    });
}
