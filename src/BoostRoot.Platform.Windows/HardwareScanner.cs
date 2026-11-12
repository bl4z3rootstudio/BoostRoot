using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using BoostRoot.Core.Hardware;
using BoostRoot.Core.Platform;

namespace BoostRoot.Platform.Windows;

/// <summary>Builds a <see cref="HardwareInfo"/> snapshot using registry, SMBIOS and display APIs (no WMI).</summary>
public static partial class HardwareScanner
{
    public static HardwareInfo Scan(IRegistry registry)
    {
        var sysDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        var freePct = sysDrive.IsReady && sysDrive.TotalSize > 0 ? 100.0 * sysDrive.AvailableFreeSpace / sysDrive.TotalSize : 100;
        return new HardwareInfo(
            RegistryHardwareReader.CpuName(registry),
            Environment.ProcessorCount,
            RegistryHardwareReader.Gpus(registry),
            TotalRamGb(),
            SmbiosMemory.Parse(ReadSmbios()),
            Displays(),
            freePct,
            null,
            Networks(),
            Environment.OSVersion.Version.Build);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetSystemFirmwareTable(uint provider, uint tableId, Span<byte> buffer, uint size);

    private static double TotalRamGb()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? Math.Round(m.ullTotalPhys / 1073741824.0, 1) : 0;
    }

    private static byte[] ReadSmbios()
    {
        const uint rsmb = 0x52534D42; // 'RSMB'
        var size = GetSystemFirmwareTable(rsmb, 0, [], 0);
        if (size == 0) return [];
        var buf = new byte[size];
        return GetSystemFirmwareTable(rsmb, 0, buf, size) == size ? buf : [];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICEW
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

#pragma warning disable SYSLIB1054 // ByValTStr structs need classic marshalling
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? device, uint devNum, ref DISPLAY_DEVICEW dd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string device, int modeNum, ref DEVMODEW dm);
#pragma warning restore SYSLIB1054

    private static IReadOnlyList<DisplayInfo> Displays()
    {
        var list = new List<DisplayInfo>();
        var dd = new DISPLAY_DEVICEW { cb = Marshal.SizeOf<DISPLAY_DEVICEW>() };
        for (uint i = 0; EnumDisplayDevicesW(null, i, ref dd, 0); i++, dd.cb = Marshal.SizeOf<DISPLAY_DEVICEW>())
        {
            if ((dd.StateFlags & 1) == 0) continue; // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
            var dm = new DEVMODEW { dmSize = (short)Marshal.SizeOf<DEVMODEW>() };
            if (!EnumDisplaySettingsW(dd.DeviceName, -1, ref dm)) continue; // ENUM_CURRENT_SETTINGS
            int w = dm.dmPelsWidth, h = dm.dmPelsHeight, cur = dm.dmDisplayFrequency, max = cur;
            for (var mode = 0; EnumDisplaySettingsW(dd.DeviceName, mode, ref dm); mode++)
                if (dm.dmDisplayFrequency > max) max = dm.dmDisplayFrequency;
            list.Add(new DisplayInfo(dd.DeviceString, w, h, cur, max));
        }
        return list;
    }

    private static IReadOnlyList<NetInfo> Networks() =>
        [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet)
            .Where(n => !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            .Select(n => new NetInfo(n.Description, n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, n.Speed / 1_000_000))];

    public static ulong AvailableRamBytes()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? m.ullAvailPhys : 0;
    }
}
