using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReController.App.Infrastructure;
using ReController.App.Services;
using ReController.Core.Input;
using ReController.Core.Text;
using ReController.Platform.Windows.Runtime;
using ReController.Platform.Windows.Services;
using Serilog;

namespace ReController.App.ViewModels;

public sealed record ModifierOption(Control Control, string Name);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly RemapService _remap;

    public SettingsViewModel(SettingsService settings, RemapService remap)
    {
        _settings = settings;
        _remap = remap;
        _startWithWindows = SafeAutoStartState();
        RefreshDrivers();
    }

    private Core.Settings.AppSettings S => _settings.Current;

    public IReadOnlyList<AccentPreset> Accents => ThemeManager.Presets;
    public IReadOnlyList<int> PollingRates { get; } = Core.Settings.AppSettings.SupportedPollingRates;

    public IReadOnlyList<ModifierOption> SlotModifiers { get; } =
    [
        new(Control.None, "Disabled"),
        new(Control.Guide, "Guide"),
        new(Control.Back, Names.Long(Control.Back)),
        new(Control.Start, Names.Long(Control.Start)),
        new(Control.LeftStickClick, Names.Long(Control.LeftStickClick)),
        new(Control.RightStickClick, Names.Long(Control.RightStickClick)),
    ];

    public string Accent
    {
        get => S.Accent;
        set
        {
            if (S.Accent == value) return;
            S.Accent = value;
            ThemeManager.Apply(value);
            Save();
        }
    }

    [ObservableProperty] private bool _startWithWindows;

    partial void OnStartWithWindowsChanged(bool value)
    {
        try
        {
            AutoStart.Set(value);
            S.StartWithWindows = value;
            Save();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not change autostart");
        }
    }

    public bool StartMinimized { get => S.StartMinimized; set { S.StartMinimized = value; Save(); } }
    public bool CloseToTray { get => S.CloseToTray; set { S.CloseToTray = value; Save(); } }
    public bool AutoProfileSwitching { get => S.AutoProfileSwitching; set { S.AutoProfileSwitching = value; Save(); } }

    public int PollingRate
    {
        get => S.PollingRateHz;
        set
        {
            S.PollingRateHz = value;
            _remap.SetPollingRate(value);
            Save();
        }
    }

    public ModifierOption SlotModifier
    {
        get => SlotModifiers.FirstOrDefault(m => m.Control == S.SlotSwitchModifier) ?? SlotModifiers[0];
        set
        {
            if (value is null) return;
            S.SlotSwitchModifier = value.Control;
            _remap.SetSlotSwitchModifier(value.Control);
            Save();
        }
    }

    [ObservableProperty] private bool _viGEmInstalled;
    [ObservableProperty] private string _viGEmVersion = "";
    [ObservableProperty] private bool _hidHideInstalled;
    [ObservableProperty] private string _hidHideVersion = "";

    public string DataFolder => AppPaths.Root;
    public bool IsPortable => AppPaths.IsPortable;
    public string Version => AppInfo.Version;

    [RelayCommand]
    public void RefreshDrivers()
    {
        var v = DriverInfo.ViGEmBus();
        ViGEmInstalled = v.Installed;
        ViGEmVersion = v.Installed ? (v.Version is null ? "Installed" : $"Installed · v{v.Version}") : "Not installed";
        var h = DriverInfo.HidHide();
        HidHideInstalled = h.Installed;
        HidHideVersion = h.Installed ? (h.Version is null ? "Installed" : $"Installed · v{h.Version}") : "Not installed";
    }

    [RelayCommand] private static void OpenViGEmDownload() => Open(DriverInfo.ViGEmBusDownloadUrl);
    [RelayCommand] private static void OpenHidHideDownload() => Open(DriverInfo.HidHideDownloadUrl);
    [RelayCommand] private static void OpenLogs() => Open(AppPaths.Logs);
    [RelayCommand] private static void OpenDataFolder() => Open(AppPaths.Root);

    internal static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex, "Could not open {Target}", target); }
    }

    private void Save() => _settings.Save();

    private static bool SafeAutoStartState()
    {
        try { return AutoStart.IsEnabled; } catch (Exception) { return false; }
    }
}

public sealed partial class HidHideDeviceViewModel(HidHideDevice device, bool hidden) : ObservableObject
{
    public HidHideDevice Device { get; } = device;
    public bool OriginallyHidden { get; } = hidden;
    [ObservableProperty] private bool _isHidden = hidden;
}

/// <summary>Manages which physical controllers HidHide hides from games.</summary>
public sealed partial class HidHideViewModel : ObservableObject
{
    public ObservableCollection<HidHideDeviceViewModel> Devices { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _cloakEnabled = true;
    [ObservableProperty] private string? _status;

    public bool IsInstalled => HidHideClient.IsInstalled;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = null;
        Status = "Waiting for administrator approval…";
        try
        {
            var snap = await HidHideClient.QueryAsync();
            Devices.Clear();
            var hidden = new HashSet<string>(snap.HiddenInstancePaths, StringComparer.OrdinalIgnoreCase);
            foreach (var d in snap.GamingDevices.Where(d => d.Present)) Devices.Add(new HidHideDeviceViewModel(d, hidden.Contains(d.InstancePath)));
            CloakEnabled = snap.CloakEnabled;
            IsLoaded = true;
            Status = Devices.Count == 0 ? "No gaming devices found." : null;
        }
        catch (OperationCanceledException)
        {
            Status = null;
            Error = "Administrator permission is required to read HidHide settings.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HidHide query failed");
            Status = null;
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = null;
        Status = "Applying…";
        try
        {
            var hide = Devices.Where(d => d.IsHidden && !d.OriginallyHidden).Select(d => d.Device.InstancePath).ToList();
            var unhide = Devices.Where(d => !d.IsHidden && d.OriginallyHidden).Select(d => d.Device.InstancePath).ToList();
            await HidHideClient.ApplyAsync(hide, unhide, CloakEnabled);
            Status = "Saved. Reconnect the controller (or restart the game) for changes to take effect.";
            IsBusy = false;
            await LoadAsync();
            Status = "Saved. Reconnect the controller (or restart the game) for changes to take effect.";
        }
        catch (OperationCanceledException)
        {
            Status = null;
            Error = "Administrator permission was declined.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HidHide apply failed");
            Status = null;
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Set this to the public repository URL once the project is published.</summary>
    public const string RepositoryUrl = "";
}
