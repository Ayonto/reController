using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReController.Platform.Windows.Services;
using Serilog;

namespace ReController.App.ViewModels;

public sealed partial class DriverRowViewModel(DriverPackage package, string purpose, bool required) : ObservableObject
{
    public DriverPackage Package { get; } = package;
    public string Name => Package.DisplayName;
    public string Purpose { get; } = purpose;
    public bool Required { get; } = required;
    public bool Bundled { get; } = DriverSetup.IsBundled(package);

    [ObservableProperty] private DriverHealth _health;
    [ObservableProperty] private bool _selected;
    [ObservableProperty] private string? _result;

    public bool IsReady => Health == DriverHealth.Ready;

    public string StatusText => Health switch
    {
        DriverHealth.Ready => "Installed",
        DriverHealth.Broken => "Needs repair",
        _ => "Not installed",
    };

    partial void OnHealthChanged(DriverHealth value)
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(StatusText));
    }
}

/// <summary>The one-time driver setup shown on first run (or whenever a driver is missing / broken).</summary>
public sealed partial class DriverSetupViewModel : ObservableObject
{
    private readonly Action _onInstalled;

    public DriverSetupViewModel(Action onInstalled)
    {
        _onInstalled = onInstalled;
        ViGEm = new DriverRowViewModel(DriverSetup.ViGEmBus, "Creates the virtual Xbox controller that games see. Required.", required: true);
        HidHide = new DriverRowViewModel(DriverSetup.HidHide, "Hides your real controller from games so buttons don't register twice.", required: false);
        foreach (var r in Rows)
        {
            r.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(DriverRowViewModel.Selected)) return;
                OnPropertyChanged(nameof(CanInstall));
                InstallCommand.NotifyCanExecuteChanged();
            };
        }
        Refresh();
    }

    public DriverRowViewModel ViGEm { get; }
    public DriverRowViewModel HidHide { get; }
    public IReadOnlyList<DriverRowViewModel> Rows => [ViGEm, HidHide];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _rebootRequired;

    public bool AllReady => ViGEm.IsReady && HidHide.IsReady;
    public bool AnyMissing => !AllReady;
    public bool CanInstall => !IsBusy && Rows.Any(r => r.Selected && r.Bundled);
    public bool IsBundled => ViGEm.Bundled && HidHide.Bundled;

    /// <summary>Re-checks driver state; pre-selects whatever needs installing.</summary>
    public void Refresh()
    {
        foreach (var r in Rows)
        {
            r.Health = r.Package == DriverSetup.ViGEmBus ? DriverSetup.ViGEmHealth() : DriverSetup.HidHideHealth();
            r.Selected = !r.IsReady && r.Bundled;
        }
        OnPropertyChanged(nameof(AllReady));
        OnPropertyChanged(nameof(AnyMissing));
        OnPropertyChanged(nameof(CanInstall));
        InstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstall));
        InstallCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        IsBusy = true;
        Error = null;
        Status = "Installing… approve the Windows permission prompt to continue.";
        foreach (var r in Rows) r.Result = null;
        try
        {
            var result = await DriverSetup.InstallAsync(ViGEm.Selected, HidHide.Selected);
            foreach (var step in result.Steps)
            {
                var row = Rows.FirstOrDefault(r => r.Package.Id == step.Id);
                if (row is not null) row.Result = step.Message;
            }
            RebootRequired = result.RebootRequired;
            Refresh();

            if (!result.Ok)
            {
                Error = string.Join(" ", result.Steps.Where(s => !s.Ok).Select(s => s.Message));
                Status = null;
            }
            else if (RebootRequired || !AllReady)
            {
                // HidHide (a filter driver) and repaired ViGEmBus installs usually need a restart to become active.
                RebootRequired = true;
                Status = "Almost done — restart Windows to finish the setup.";
            }
            else
            {
                Status = "All set! Your controller is ready to be remapped.";
            }
            _onInstalled();
        }
        catch (OperationCanceledException)
        {
            Status = null;
            Error = "Windows permission was declined, so nothing was installed. Click Install to try again.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Driver setup failed");
            Status = null;
            Error = "Setup failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RestartWindows()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Restarting to finish reController setup\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not restart Windows");
            Error = "Could not restart automatically. Please restart Windows from the Start menu.";
        }
    }

    [RelayCommand] private static void OpenViGEmDownload() => SettingsViewModel.Open(DriverInfo.ViGEmBusDownloadUrl);
    [RelayCommand] private static void OpenHidHideDownload() => SettingsViewModel.Open(DriverInfo.HidHideDownloadUrl);
}
