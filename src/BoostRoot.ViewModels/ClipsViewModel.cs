using System.Collections.ObjectModel;
using BoostRoot.Core.Clips;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed partial class ClipItemViewModel(ClipFile clip) : ObservableObject
{
    public ClipFile Clip { get; } = clip;
    public string Name => Path.GetFileNameWithoutExtension(Clip.Path);
    public string Info => $"{Clip.Game} · {Clip.Created.ToLocalTime():dd.MM.yyyy HH:mm} · {ToolsViewModel.Bytes(Clip.Bytes)}";
    public bool IsImage => Clip.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    [ObservableProperty] public partial string? Thumbnail { get; set; }
}

public sealed partial class ClipsViewModel(IAppServices services, Func<AppSettings> settings) : ObservableObject
{
    public ObservableCollection<ClipItemViewModel> Clips { get; } = [];
    public ObservableCollection<string> Games { get; } = [];
    public static string AllGames => Loc.I["clips.allGames"];

    [ObservableProperty] public partial bool FfmpegInstalled { get; set; } = services.FfmpegInstalled;
    [ObservableProperty] public partial double InstallProgress { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial string? GameFilter { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] public partial ClipItemViewModel? Selected { get; set; }
    [ObservableProperty] public partial double TrimStart { get; set; }
    [ObservableProperty] public partial double TrimEnd { get; set; }
    [ObservableProperty] public partial double Duration { get; set; }
    public bool HasSelection => Selected is not null;
    public string Folder => services.ClipsFolder;
    private IReadOnlyList<ClipFile> _all = [];

    [RelayCommand]
    public async Task InstallAsync(CancellationToken ct)
    {
        IsBusy = true;
        Status = Loc.I["clips.installing"];
        try
        {
            await services.InstallFfmpegAsync(new Progress<double>(p => InstallProgress = p), ct);
            FfmpegInstalled = services.FfmpegInstalled;
            Status = FfmpegInstalled ? Loc.I["clips.installed"] : Loc.I["clips.installFailed"];
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
        {
            Status = Loc.I["clips.installFailed"] + " (" + ex.Message + ")";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public void Refresh()
    {
        _all = services.ListClips();
        Games.Clear();
        Games.Add(AllGames);
        foreach (var g in _all.Select(c => c.Game).Distinct().Order()) Games.Add(g);
        GameFilter ??= AllGames;
        ApplyFilter();
    }

    partial void OnGameFilterChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        Clips.Clear();
        foreach (var c in _all.Where(c => GameFilter is null || GameFilter == AllGames || c.Game == GameFilter).OrderByDescending(c => c.Created))
        {
            var vm = new ClipItemViewModel(c);
            Clips.Add(vm);
            if (FfmpegInstalled && !vm.IsImage) _ = LoadThumb(vm);
            else if (vm.IsImage) vm.Thumbnail = c.Path;
        }
    }

    private async Task LoadThumb(ClipItemViewModel vm) => vm.Thumbnail = await services.ThumbnailAsync(vm.Clip);

    partial void OnSelectedChanged(ClipItemViewModel? value)
    {
        Status = null;
        Duration = TrimStart = TrimEnd = 0;
        if (value is not null && !value.IsImage) _ = LoadDuration(value);
    }

    private async Task LoadDuration(ClipItemViewModel vm)
    {
        if (await services.ClipDurationAsync(vm.Clip.Path) is { } d && Selected == vm) { Duration = Math.Round(d, 1); TrimEnd = Duration; }
    }

    private async Task Busy(Func<Task<string?>> work, string okKey)
    {
        IsBusy = true;
        try
        {
            var path = await work();
            Status = path is null ? Loc.I["clips.failed"] : Loc.I.Format(okKey, Path.GetFileName(path));
            Refresh();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public Task TrimAsync() => Selected is null || TrimEnd <= TrimStart ? Task.CompletedTask
        : Busy(() => services.TrimClipAsync(Selected.Clip.Path, TrimStart, TrimEnd), "clips.trimmed");

    [RelayCommand]
    public Task VerticalAsync() => Selected is null ? Task.CompletedTask : Busy(() => services.VerticalClipAsync(Selected.Clip.Path), "clips.vertical");

    [RelayCommand]
    public async Task SendDiscordAsync(CancellationToken ct)
    {
        var hook = settings().ClipsOrDefault.DiscordWebhook;
        if (Selected is null) return;
        if (string.IsNullOrWhiteSpace(hook)) { Status = Loc.I["clips.noWebhook"]; return; }
        IsBusy = true;
        Status = Loc.I["clips.sending"];
        try { Status = await services.SendToDiscordAsync(hook, Selected.Clip.Path, ct) ? Loc.I["clips.sent"] : Loc.I["clips.sendFailed"]; }
        finally { IsBusy = false; }
    }

    [RelayCommand] public void Open() { if (Selected is not null) services.OpenUrl(Selected.Clip.Path); }
    [RelayCommand] public void OpenFolder() => services.OpenUrl(Folder);
}
