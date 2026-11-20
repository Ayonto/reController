using System.Windows.Controls;
using System.Windows.Input;
using ReController.App.ViewModels;

namespace ReController.App.Views;

public partial class BindingEditorView : UserControl
{
    public BindingEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            // Jump straight into key capture when a fresh keyboard mapping is started.
            if (DataContext is BindingEditorViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(BindingEditorViewModel.IsCapturing) && vm.IsCapturing)
                        Dispatcher.BeginInvoke(() => CaptureBox.Focus());
                };
            }
        };
    }

    private BindingEditorViewModel? Vm => DataContext as BindingEditorViewModel;

    private void OnCaptureClick(object sender, MouseButtonEventArgs e)
    {
        Vm?.StartCaptureCommand.Execute(null);
        CaptureBox.Focus();
        Keyboard.Focus(CaptureBox);
        e.Handled = true;
    }

    private void OnCaptureKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { IsCapturing: true } vm) return;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        if (key is Key.None or Key.Snapshot) return;
        vm.CaptureKey((ushort)KeyInterop.VirtualKeyFromKey(key));
        e.Handled = true;
    }

    private void OnCaptureKeyUp(object sender, KeyEventArgs e)
    {
        // Print Screen only produces a key-up in WPF.
        if (Vm is { IsCapturing: true } vm && e.Key == Key.Snapshot)
        {
            vm.CaptureKey((ushort)KeyInterop.VirtualKeyFromKey(Key.Snapshot));
            e.Handled = true;
        }
    }

    private void OnCaptureLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Vm is { } vm) vm.IsCapturing = false;
    }
}
