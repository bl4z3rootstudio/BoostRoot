using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BoostRoot.App;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnDragArea(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnToggleMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
