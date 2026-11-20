using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ReController.App.Infrastructure;
using ReController.App.Services;
using ReController.App.ViewModels;

namespace ReController.App.Views;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;

    public MainWindow(MainViewModel vm, SettingsService settings)
    {
        InitializeComponent();
        DataContext = vm;
        _settings = settings;
        RestorePlacement();
        SourceInitialized += (_, _) => NativeWindow.ApplyModernFrame(this);
        StateChanged += (_, _) => { UpdateChrome(); vm.SetUiActive(IsVisible && WindowState != WindowState.Minimized); };
        IsVisibleChanged += (_, _) => vm.SetUiActive(IsVisible && WindowState != WindowState.Minimized);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Raised when the close button hid the window to the tray instead of exiting.</summary>
    public event Action? HiddenToTray;

    /// <summary>True when closing should really exit instead of hiding to the tray.</summary>
    public bool AllowClose { get; set; }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (Vm.IsOverlayOpen) { Vm.CloseOverlaysCommand.Execute(null); e.Handled = true; }
        else if (Vm.IsEditorOpen && Vm.Editor is { IsCapturing: false }) { Vm.CloseEditor(); e.Handled = true; }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void UpdateChrome()
    {
        // A maximised chromeless window overhangs the screen by the resize border; pad it back in.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        Root.BorderThickness = WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(1);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();
        if (!AllowClose && _settings.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }
        base.OnClosing(e);
    }

    private void RestorePlacement()
    {
        var p = _settings.Current.Window;
        if (p.Width >= MinWidth && p.Height >= MinHeight)
        {
            Width = p.Width;
            Height = p.Height;
        }
        if (!double.IsNaN(p.Left) && !double.IsNaN(p.Top))
        {
            // Only restore a position that is still on a visible screen.
            var area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (area.Contains(new Point(p.Left + 80, p.Top + 20)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = p.Left;
                Top = p.Top;
            }
        }
        if (p.Maximized) WindowState = WindowState.Maximized;
        UpdateChrome();
    }

    private void SavePlacement()
    {
        var p = _settings.Current.Window;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (r.Width > 0 && r.Height > 0)
        {
            p.Left = r.Left;
            p.Top = r.Top;
            p.Width = r.Width;
            p.Height = r.Height;
        }
        p.Maximized = WindowState == WindowState.Maximized;
        _settings.Save();
    }
}
