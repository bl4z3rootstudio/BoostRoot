using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BoostRoot.ViewModels;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    /// <summary>Folder picker instead of typing a path.</summary>
    private async void OnPickClipFolder(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp || Vm is not { } vm) return;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.I["settings.clipFolder"], AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) vm.ClipFolder = path;
    }

    private void OnResetClipFolder(object? sender, RoutedEventArgs e) { if (Vm is { } vm) vm.ClipFolder = ""; }
}
