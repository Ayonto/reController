using System.Windows;
using Microsoft.Win32;
using ReController.App.Infrastructure;
using ReController.App.ViewModels;

namespace ReController.App.Views;

public partial class DialogWindow : Window
{
    private DialogWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeWindow.ApplyModernFrame(this);
    }

    private static Window? Owner_ => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public static string? Prompt(string title, string message, string initial)
    {
        var d = new DialogWindow { Owner = Owner_ };
        d.TitleText.Text = title;
        d.MessageText.Text = message;
        d.InputBox.Visibility = Visibility.Visible;
        d.InputBox.Text = initial;
        d.Loaded += (_, _) => { d.InputBox.Focus(); d.InputBox.SelectAll(); };
        return d.ShowDialog() == true ? d.InputBox.Text : null;
    }

    public static bool Confirm(string title, string message, string confirmText, bool danger)
    {
        var d = new DialogWindow { Owner = Owner_ };
        d.TitleText.Text = title;
        d.MessageText.Text = message;
        d.OkButton.Content = confirmText;
        if (danger) d.OkButton.Style = (Style)d.FindResource("DangerButton");
        return d.ShowDialog() == true;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class DialogService : IDialogService
{
    public string? Prompt(string title, string message, string initial = "") => DialogWindow.Prompt(title, message, initial);

    public bool Confirm(string title, string message, string confirmText = "OK", bool danger = false) =>
        DialogWindow.Confirm(title, message, confirmText, danger);

    public string? OpenFile(string filter)
    {
        var d = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return d.ShowDialog() == true ? d.FileName : null;
    }

    public string? SaveFile(string filter, string defaultName)
    {
        var d = new SaveFileDialog { Filter = filter, FileName = defaultName, OverwritePrompt = true };
        return d.ShowDialog() == true ? d.FileName : null;
    }
}
