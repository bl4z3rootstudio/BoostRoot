using System.Collections.ObjectModel;
using BoostRoot.Core.Updates;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed partial class AppUpdateRow(AppUpdate u) : ObservableObject
{
    public AppUpdate U { get; } = u;
    public string Name => U.Name;
    public string Versions => $"{U.Current} → {U.Available}";
    [ObservableProperty] public partial string? State { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool Done { get; set; }
}

public sealed partial class WinUpdateRow(WinUpdate u) : ObservableObject
{
    public WinUpdate U { get; } = u;
    public string Title => U.Title;
    public string Kind => U.IsDriver ? Loc.I["upd.driver"] : U.Optional ? Loc.I["upd.optional"] : Loc.I["upd.important"];
    public string Size => U.SizeBytes > 0 ? ToolsViewModel.Bytes(U.SizeBytes) : "";
    public bool Unneeded => U.LooksUnneeded;
    [ObservableProperty] public partial bool IsChecked { get; set; } = !u.LooksUnneeded;
    [ObservableProperty] public partial bool Hidden { get; set; }
}

public sealed partial class BloatRow(BloatApp b) : ObservableObject
{
    public BloatApp B { get; } = b;
    public string Name => B.Friendly;
    [ObservableProperty] public partial bool Removed { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
}

/// <summary>One place to update everything: apps (winget), Windows + drivers (Windows Update), GPU driver, and remove bloat.</summary>
public sealed partial class UpdatesViewModel(IAppServices services) : ObservableObject
{
    public ObservableCollection<AppUpdateRow> Apps { get; } = [];
    public ObservableCollection<WinUpdateRow> Windows { get; } = [];
    public ObservableCollection<BloatRow> Bloat { get; } = [];
    [ObservableProperty] public partial bool Scanning { get; set; }
    [ObservableProperty] public partial bool Installing { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial string? AppsNote { get; set; }
    [ObservableProperty] public partial string? WindowsNote { get; set; }
    [ObservableProperty] public partial string? GpuText { get; set; }
    [ObservableProperty] public partial bool GpuOld { get; set; }
    [ObservableProperty] public partial bool Scanned { get; set; }
    public int Total => Apps.Count(a => !a.Done) + Windows.Count(w => !w.Hidden) + (GpuOld ? 1 : 0);
    public string Summary => Scanned ? (Total == 0 ? Loc.I["upd.allGood"] : Loc.I.Format("upd.found", Total)) : Loc.I["upd.notScanned"];

    private void Refresh() { OnPropertyChanged(nameof(Total)); OnPropertyChanged(nameof(Summary)); }

    [RelayCommand]
    public async Task ScanAsync(CancellationToken ct)
    {
        if (Scanning) return;
        Scanning = true;
        Status = Loc.I["upd.scanning"];
        Apps.Clear(); Windows.Clear(); Bloat.Clear();
        AppsNote = WindowsNote = null;
        try
        {
            var apps = Safe(services.ScanAppUpdatesAsync(ct), "upd.wingetMissing", n => AppsNote = n);
            var win = Safe(services.ScanWindowsUpdatesAsync(ct), "upd.wuFailed", n => WindowsNote = n);
            var bloat = Safe(services.ScanBloatAsync(ct), null, _ => { });
            foreach (var a in await apps) Apps.Add(new AppUpdateRow(a));
            foreach (var w in (await win).OrderByDescending(w => w.IsDriver).ThenBy(w => w.Optional)) Windows.Add(new WinUpdateRow(w));
            foreach (var b in await bloat) Bloat.Add(new BloatRow(b));
            if (services.CheckDriver() is { } d)
            {
                GpuOld = d.IsOld;
                GpuText = d.AgeDays < 0 ? d.Gpu.Name : Loc.I.Format(d.IsOld ? "upd.gpuOld" : "upd.gpuOk", d.Gpu.Name, d.AgeDays);
            }
            Scanned = true;
            Status = null;
        }
        catch (OperationCanceledException) { Status = null; }
        finally { Scanning = false; Refresh(); }
    }

    private static async Task<IReadOnlyList<T>> Safe<T>(Task<IReadOnlyList<T>> t, string? noteKey, Action<string> note)
    {
        try { return await t; }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException or System.Text.Json.JsonException or TimeoutException)
        {
            if (noteKey is not null) note(Loc.I[noteKey]);
            return [];
        }
    }

    [RelayCommand]
    public async Task UpgradeAppAsync(AppUpdateRow row)
    {
        if (row.Busy || row.Done) return;
        row.Busy = true;
        row.State = Loc.I["upd.updating"];
        try
        {
            var ok = await services.UpgradeAppAsync(row.U, CancellationToken.None);
            row.Done = ok;
            row.State = ok ? Loc.I["upd.updated"] : Loc.I["upd.failed"];
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException) { row.State = Loc.I["upd.failed"]; }
        finally { row.Busy = false; Refresh(); }
    }

    [RelayCommand]
    public async Task UpgradeAllAppsAsync()
    {
        Installing = true;
        try { foreach (var a in Apps.Where(a => !a.Done).ToList()) await UpgradeAppAsync(a); }
        finally { Installing = false; }
    }

    [RelayCommand]
    public async Task InstallWindowsAsync()
    {
        var picked = Windows.Where(w => w.IsChecked && !w.Hidden).ToList();
        if (picked.Count == 0) return;
        Installing = true;
        Status = Loc.I.Format("upd.installingWin", picked.Count);
        try
        {
            var (ok, reboot) = await services.InstallWindowsUpdatesAsync([.. picked.Select(p => p.U)], CancellationToken.None);
            Status = !ok ? Loc.I["upd.failed"] : reboot ? Loc.I["upd.reboot"] : Loc.I["upd.winDone"];
            if (ok) foreach (var p in picked) Windows.Remove(p);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or OperationCanceledException) { Status = Loc.I["upd.failed"]; }
        finally { Installing = false; Refresh(); }
    }

    [RelayCommand]
    public async Task HideAsync(WinUpdateRow row)
    {
        try { await services.HideWindowsUpdateAsync(row.U, CancellationToken.None); row.Hidden = true; Windows.Remove(row); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { Status = Loc.I["upd.failed"]; }
        Refresh();
    }

    [RelayCommand]
    public async Task RemoveBloatAsync(BloatRow row)
    {
        row.Busy = true;
        try { row.Removed = await services.RemoveBloatAsync(row.B, CancellationToken.None); if (row.Removed) Bloat.Remove(row); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { Status = Loc.I["upd.failed"]; }
        finally { row.Busy = false; }
    }

    [RelayCommand]
    public void OpenGpu() { if (services.CheckDriver()?.DownloadUrl is { } u) services.OpenUrl(u); }

    [RelayCommand]
    public async Task UpdateEverythingAsync()
    {
        await UpgradeAllAppsAsync();
        await InstallWindowsAsync();
        if (GpuOld) OpenGpu();
    }
}
