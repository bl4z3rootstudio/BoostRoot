using BoostRoot.Core.Hardware;

namespace BoostRoot.Core.Tools;

public sealed record DriverStatus(GpuInfo Gpu, bool IsOld, int AgeDays, string DownloadUrl);

public static class DriverCheck
{
    public static DriverStatus? Check(HardwareInfo hw, DateOnly today)
    {
        if (hw.MainGpu is not { } g) return null;
        var age = g.DriverDate is { } d ? today.DayNumber - d.DayNumber : -1;
        var url = g.Vendor switch
        {
            GpuVendor.Nvidia => "https://www.nvidia.com/Download/index.aspx",
            GpuVendor.Amd => "https://www.amd.com/en/support/download/drivers.html",
            GpuVendor.Intel => "https://www.intel.com/content/www/us/en/download-center/home.html",
            _ => "https://support.microsoft.com/windows/update-drivers-manually-in-windows-ec62f46c-ff14-c91d-eead-d7126dc1f7b6",
        };
        return new(g, age > 183, age, url);
    }
}
