using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReController.App.ViewModels;

namespace ReController.App.Views;

public partial class SticksPage : UserControl
{
    public SticksPage() => InitializeComponent();
}

public partial class TesterPage : UserControl
{
    public TesterPage() => InitializeComponent();
}

public partial class GamePage : UserControl
{
    public GamePage() => InitializeComponent();
}

public partial class Overlays : UserControl
{
    public Overlays() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnBackdrop(object sender, MouseButtonEventArgs e) => Vm?.CloseOverlaysCommand.Execute(null);

    private void OnAccentChecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && Vm is { } vm) vm.Settings.Accent = name;
    }

    private void OnAccentLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string name } rb && Vm is { } vm)
            rb.IsChecked = string.Equals(vm.Settings.Accent, name, StringComparison.OrdinalIgnoreCase);
    }
}
