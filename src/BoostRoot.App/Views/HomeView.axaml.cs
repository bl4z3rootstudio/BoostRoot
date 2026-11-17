using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BoostRoot.ViewModels;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.App.Views;

public sealed partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    private HomeViewModel? Vm => DataContext as HomeViewModel;

    /// <summary>"Add game": pick the game's .exe when the library scan did not find it.</summary>
    private async void OnAddGame(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp || Vm is not { } vm) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.I["home.addGame"], AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("exe") { Patterns = ["*.exe"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        string name;
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            name = !string.IsNullOrWhiteSpace(info.ProductName) && !info.ProductName.Contains("Unreal", StringComparison.OrdinalIgnoreCase) ? info.ProductName.Trim()
                : Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception) { name = Path.GetFileNameWithoutExtension(path); }
        vm.AddManualGame(path, name);
    }

    /// <summary>"Change cover": any picture from disk.</summary>
    private async void OnPickCover(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp || Vm is not { } vm) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.I["home.changeCover"], AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) vm.SetCover(path);
    }
}
