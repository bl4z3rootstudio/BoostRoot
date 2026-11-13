using CommunityToolkit.Mvvm.ComponentModel;

namespace BoostRoot.ViewModels;

/// <summary>Mouse / keyboard / monitor tests. The view feeds raw input in; nothing here touches the system.</summary>
public sealed partial class TestsViewModel : ObservableObject
{
    [ObservableProperty] public partial string MouseHz { get; set; } = "—";
    [ObservableProperty] public partial string MousePeak { get; set; } = "—";
    [ObservableProperty] public partial string MonitorHz { get; set; } = "—";
    [ObservableProperty] public partial string KeysTested { get; set; } = "0";
    [ObservableProperty] public partial string Rollover { get; set; } = "0";
    [ObservableProperty] public partial string LastKey { get; set; } = "—";
    [ObservableProperty] public partial string Clicks { get; set; } = "0 / 0 / 0";
}
