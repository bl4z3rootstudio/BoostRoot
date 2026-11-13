using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using BoostRoot.ViewModels;

namespace BoostRoot.App.Views;

public sealed partial class MonitorView : UserControl
{
    private readonly DispatcherTimer _timer;

    public MonitorView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => (DataContext as MonitorViewModel)?.Tick());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _timer.Stop(); base.OnDetachedFromVisualTree(e); }
}
