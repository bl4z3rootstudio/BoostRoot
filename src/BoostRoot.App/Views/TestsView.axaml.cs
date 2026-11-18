using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BoostRoot.Core.Ui;
using BoostRoot.ViewModels;

namespace BoostRoot.App.Views;

public sealed partial class TestsView : UserControl
{
    // "|" = gap between key blocks. Layouts: 60, 68 (65%), 75, TKL (80%), 100 (full size).
    private static string[][] Rows(string layout)
    {
        string[] f = ["Escape", "|", "F1", "F2", "F3", "F4", "|", "F5", "F6", "F7", "F8", "|", "F9", "F10", "F11", "F12"];
        string[] r1 = ["OemTilde", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0", "OemMinus", "OemPlus", "Back"];
        string[] r2 = ["Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "OemOpenBrackets", "OemCloseBrackets", "OemPipe"];
        string[] r3 = ["CapsLock", "A", "S", "D", "F", "G", "H", "J", "K", "L", "OemSemicolon", "OemQuotes", "Enter"];
        string[] r4 = ["LeftShift", "Z", "X", "C", "V", "B", "N", "M", "OemComma", "OemPeriod", "OemQuestion", "RightShift"];
        string[] r5 = ["LeftCtrl", "LWin", "LeftAlt", "Space", "RightAlt", "Apps", "RightCtrl"];
        return layout switch
        {
            "60" => [r1, r2, r3, r4, r5],
            "68" => [[.. r1, "Delete"], [.. r2, "PageUp"], [.. r3, "PageDown"], [.. r4, "Up"], ["LeftCtrl", "LWin", "LeftAlt", "Space", "RightAlt", "RightCtrl", "Left", "Down", "Right"]],
            "75" => [[.. f, "Delete"], [.. r1, "Home"], [.. r2, "PageUp"], [.. r3, "PageDown"], [.. r4, "Up"], ["LeftCtrl", "LWin", "LeftAlt", "Space", "RightAlt", "RightCtrl", "Left", "Down", "Right"]],
            "tkl" => [[.. f, "|", "PrintScreen", "Scroll", "Pause"], [.. r1, "|", "Insert", "Home", "PageUp"], [.. r2, "|", "Delete", "End", "PageDown"],
                      r3, [.. r4, "|", "_", "Up"], [.. r5, "|", "Left", "Down", "Right"]],
            _ => [[.. f, "|", "PrintScreen", "Scroll", "Pause"], [.. r1, "|", "Insert", "Home", "PageUp", "|", "NumLock", "Divide", "Multiply", "Subtract"],
                  [.. r2, "|", "Delete", "End", "PageDown", "|", "NumPad7", "NumPad8", "NumPad9", "Add"], [.. r3, "|", "_", "_", "_", "|", "NumPad4", "NumPad5", "NumPad6"],
                  [.. r4, "|", "_", "Up", "_", "|", "NumPad1", "NumPad2", "NumPad3"], [.. r5, "|", "Left", "Down", "Right", "|", "NumPad0", "Decimal"]],
        };
    }

    private static readonly Dictionary<string, (string Label, double Width)> Labels = new()
    {
        ["Escape"] = ("Esc", 52), ["OemTilde"] = ("\"", 40), ["OemMinus"] = ("*", 40), ["OemPlus"] = ("-", 40), ["Back"] = ("⌫", 76),
        ["Tab"] = ("Tab", 60), ["OemOpenBrackets"] = ("Ğ", 40), ["OemCloseBrackets"] = ("Ü", 40), ["OemPipe"] = (",", 56),
        ["CapsLock"] = ("Caps", 70), ["OemSemicolon"] = ("Ş", 40), ["OemQuotes"] = ("İ", 40), ["Enter"] = ("Enter", 86),
        ["LeftShift"] = ("Shift", 92), ["OemComma"] = ("Ö", 40), ["OemPeriod"] = ("Ç", 40), ["OemQuestion"] = (".", 40), ["RightShift"] = ("Shift", 110),
        ["LeftCtrl"] = ("Ctrl", 60), ["LWin"] = ("Win", 52), ["LeftAlt"] = ("Alt", 52), ["Space"] = ("Space", 290), ["RightAlt"] = ("AltGr", 60), ["RightCtrl"] = ("Ctrl", 60),
        ["Left"] = ("←", 40), ["Up"] = ("↑", 40), ["Down"] = ("↓", 40), ["Right"] = ("→", 40),
        ["Apps"] = ("Menü", 52), ["Delete"] = ("Del", 40), ["Insert"] = ("Ins", 40), ["Home"] = ("Home", 46), ["End"] = ("End", 40),
        ["PageUp"] = ("PgUp", 46), ["PageDown"] = ("PgDn", 46), ["PrintScreen"] = ("PrtSc", 50), ["Scroll"] = ("ScrLk", 50), ["Pause"] = ("Pause", 50),
        ["NumLock"] = ("Num", 40), ["Divide"] = ("/", 40), ["Multiply"] = ("*", 40), ["Subtract"] = ("-", 40), ["Add"] = ("+", 40), ["Decimal"] = (",", 40),
        ["NumPad0"] = ("0", 86), ["NumPad1"] = ("1", 40), ["NumPad2"] = ("2", 40), ["NumPad3"] = ("3", 40), ["NumPad4"] = ("4", 40), ["NumPad5"] = ("5", 40),
        ["NumPad6"] = ("6", 40), ["NumPad7"] = ("7", 40), ["NumPad8"] = ("8", 40), ["NumPad9"] = ("9", 40),
    };

    private readonly Dictionary<Key, Border> _keys = [];
    private readonly HashSet<Key> _tested = [], _down = [];
    private readonly RateMeter _mouse = new(0.5), _frames = new(1.0);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _maxDown, _left, _right, _middle;
    private bool _attached;
    private TopLevel? _top;

    private TestsViewModel? Vm => DataContext as TestsViewModel;

    public TestsView()
    {
        InitializeComponent();
        BuildKeyboard();
        MousePad.PointerMoved += (_, e) =>
        {
            var n = Math.Max(1, e.GetIntermediatePoints(MousePad).Count);
            _mouse.Add(_clock.Elapsed.TotalSeconds, n);
            if (Vm is { } vm) { vm.MouseHz = $"{_mouse.Rate:0}"; vm.MousePeak = $"{_mouse.Peak:0}"; }
        };
        MousePad.PointerPressed += (_, e) =>
        {
            var k = e.GetCurrentPoint(MousePad).Properties.PointerUpdateKind;
            if (k == PointerUpdateKind.LeftButtonPressed) _left++;
            else if (k == PointerUpdateKind.RightButtonPressed) _right++;
            else if (k == PointerUpdateKind.MiddleButtonPressed) _middle++;
            if (Vm is { } vm) vm.Clicks = $"{_left} / {_right} / {_middle}";
        };
        DeadPixelButton.Click += (_, _) => DeadPixelWindow.Open();
        // Bigger pad = more room to move = steadier Hz reading.
        PadSize.ValueChanged += (_, e) => MousePad.Height = e.NewValue;
        LayoutBox.SelectionChanged += (_, _) =>
        {
            _layout = (LayoutBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "100";
            BuildKeyboard();
            foreach (var k in _tested) if (_keys.TryGetValue(k, out var b)) b.Background = new SolidColorBrush(Color.Parse("#1E5E3A"));
        };
        ResetButton.Click += (_, _) => Reset();
    }

    private static IBrush Res(string key, IBrush fallback) =>
        Application.Current?.TryGetResource(key, null, out var r) == true && r is IBrush b ? b : fallback;

    private string _layout = "100";

    private void BuildKeyboard()
    {
        Keyboard.Children.Clear();
        _keys.Clear();
        foreach (var row in Rows(_layout))
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var name in row)
            {
                if (name == "|") { panel.Children.Add(new Border { Width = 14 }); continue; }
                if (name == "_") { panel.Children.Add(new Border { Width = 40 }); continue; }
                if (!Enum.TryParse<Key>(name, out var key)) continue;
                var (label, width) = Labels.TryGetValue(name, out var l) ? l
                    : (name.StartsWith('D') && name.Length == 2 ? name[1..] : name, 40);
                var b = new Border
                {
                    Width = width, Height = 36, CornerRadius = new CornerRadius(7),
                    Background = Res("Surface2", Brushes.DimGray),
                    BorderBrush = Res("Border", Brushes.Gray), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    Transitions = [new Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(120) }],
                };
                _keys[key] = b;
                panel.Children.Add(b);
            }
            Keyboard.Children.Add(panel);
        }
    }

    private void Reset()
    {
        _tested.Clear(); _down.Clear(); _maxDown = 0; _left = _right = _middle = 0;
        _mouse.Reset();
        foreach (var b in _keys.Values) b.Background = Res("Surface2", Brushes.DimGray);
        if (Vm is { } vm) { vm.KeysTested = "0"; vm.Rollover = "0"; vm.LastKey = "—"; vm.Clicks = "0 / 0 / 0"; vm.MouseHz = vm.MousePeak = "—"; }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _top = TopLevel.GetTopLevel(this);
        _top?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _top?.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        _frames.Reset();
        _top?.RequestAnimationFrame(OnFrame);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _top?.RemoveHandler(KeyDownEvent, OnKeyDown);
        _top?.RemoveHandler(KeyUpEvent, OnKeyUp);
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Refresh rate = how often the compositor hands us a frame while we keep asking for one.</summary>
    private void OnFrame(TimeSpan t)
    {
        if (!_attached) return;
        _frames.Add(t.TotalSeconds);
        if (_frames.Rate > 0 && Vm is { } vm) vm.MonitorHz = RateMeter.SnapHz(_frames.Rate).ToString();
        _top?.RequestAnimationFrame(OnFrame);
    }

    private void OnKeyDown(object? s, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? Key.LeftAlt : e.Key;
        if (key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return; // let Alt+F4 close the window
        e.Handled = true; // Tab/arrows/Space must not move focus or click buttons while testing
        _down.Add(key);
        _maxDown = Math.Max(_maxDown, _down.Count);
        if (_keys.TryGetValue(key, out var b)) b.Background = new SolidColorBrush(Color.Parse("#2F9E5B"));
        _tested.Add(key);
        if (Vm is { } vm) { vm.KeysTested = $"{_tested.Count}"; vm.Rollover = $"{_maxDown}"; vm.LastKey = key.ToString(); }
    }

    private void OnKeyUp(object? s, KeyEventArgs e)
    {
        _down.Remove(e.Key == Key.System ? Key.LeftAlt : e.Key);
        if (_keys.TryGetValue(e.Key, out var b)) b.Background = new SolidColorBrush(Color.Parse("#1E5E3A"));
    }
}

/// <summary>Full-screen solid colours for spotting dead/stuck pixels. Click = next colour, Esc = close.</summary>
public sealed class DeadPixelWindow : Window
{
    private static readonly Color[] Colors = [Avalonia.Media.Colors.Black, Avalonia.Media.Colors.White, Color.Parse("#FF0000"), Color.Parse("#00FF00"), Color.Parse("#0000FF"), Color.Parse("#808080")];
    private int _i;

    public static void Open() => new DeadPixelWindow().Show();

    private DeadPixelWindow()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowState = WindowState.FullScreen;
        Topmost = true;
        Cursor = new Cursor(StandardCursorType.None);
        Background = new SolidColorBrush(Colors[0]);
        PointerPressed += (_, _) => { if (++_i >= Colors.Length) Close(); else Background = new SolidColorBrush(Colors[_i]); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key is Key.Space or Key.Right) { if (++_i >= Colors.Length) Close(); else Background = new SolidColorBrush(Colors[_i]); } };
    }
}
