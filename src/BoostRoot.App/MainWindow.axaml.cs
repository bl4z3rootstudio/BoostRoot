using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BoostRoot.App;

public sealed partial class MainWindow : Window
{
    private const string MaxData = "M0 0h10v10H0zM1.2 1.2v7.6h7.6V1.2z";
    private const string RestoreData = "M2 0h8v8H8.8V1.2H2zM0 2h8v8H0zM1.2 3.2v5.6h5.6V3.2z";

    public MainWindow()
    {
        InitializeComponent();
        // Maximize ↔ restore glyph follows the window state (also when Windows snaps/maximizes it).
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                MaxIcon.Data = Avalonia.Media.Geometry.Parse(WindowState == WindowState.Maximized ? RestoreData : MaxData);
        };
    }

    private void OnDragArea(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnToggleMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
