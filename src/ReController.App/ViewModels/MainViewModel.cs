using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReController.App.Services;
using ReController.Core.Input;
using ReController.Core.Mapping;
using ReController.Core.Profiles;
using ReController.Core.Settings;
using ReController.Core.Text;
using ReController.Platform.Windows.Output;
using ReController.Platform.Windows.Runtime;
using ReController.Platform.Windows.Services;
using Serilog;

namespace ReController.App.ViewModels;

public interface IDialogService
{
    string? Prompt(string title, string message, string initial = "");
    bool Confirm(string title, string message, string confirmText = "OK", bool danger = false);
    string? OpenFile(string filter);
    string? SaveFile(string filter, string defaultName);
}

public sealed partial class AxisViewModel(string name, bool centered) : ObservableObject
{
    public const double BarWidth = 150;
    public string Name { get; } = name;
    public bool Centered { get; } = centered;

    [ObservableProperty] private double _value;
    [ObservableProperty] private double _barLeft;
    [ObservableProperty] private double _barSize;

    partial void OnValueChanged(double value)
    {
        if (Centered)
        {
            double half = BarWidth / 2, w = Math.Abs(value) * half;
            BarLeft = value < 0 ? half - w : half;
            BarSize = w;
        }
        else
        {
            BarLeft = 0;
            BarSize = Math.Clamp(value, 0, 1) * BarWidth;
        }
        OnPropertyChanged(nameof(Display));
    }

    public string Display => Value.ToString(Centered ? "+0.00;-0.00; 0.00" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed partial class LayerItemViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;
    public string Name { get; } = index == 0 ? "Main" : $"Shift {index}";
    public string Short { get; } = index == 0 ? "M" : index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private bool _hasBindings;
}

public sealed partial class MainViewModel : ObservableObject, IBindingHost
{
    private static readonly string ProfileFilter = $"reController profile (*{ProfileStore.FileExtension})|*{ProfileStore.FileExtension}|JSON (*.json)|*.json";

    private readonly ProfileService _profiles;
    private readonly SettingsService _settings;
    private readonly RemapService _remap;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<Control, ControlBindingViewModel> _controls;
    private readonly DispatcherTimer _liveTimer;
    private readonly DispatcherTimer _toastTimer;

    public MainViewModel(ProfileService profiles, SettingsService settings, RemapService remap, IDialogService dialogs)
    {
        _profiles = profiles;
        _settings = settings;
        _remap = remap;
        _dialogs = dialogs;

        Settings = new SettingsViewModel(settings, remap);
        _controls = ControlInfo.All.ToDictionary(c => c, c => new ControlBindingViewModel(c));
        Slots = new ObservableCollection<SlotItemViewModel>(Enumerable.Range(0, DeviceAssignment.SlotCount).Select(i => new SlotItemViewModel(i)));
        Layers = new ObservableCollection<LayerItemViewModel>(Enumerable.Range(0, MappingConfig.MaxShiftLayers + 1).Select(i => new LayerItemViewModel(i)));
        InputIndicators = new ObservableCollection<IndicatorViewModel>(ControlInfo.All.Where(c => ControlInfo.KindOf(c) != ControlKind.StickDirection).Select(c => new IndicatorViewModel(Names.Short(c))));
        OutputIndicators = new ObservableCollection<IndicatorViewModel>(ControlInfo.VirtualPadTargets.Where(c => ControlInfo.KindOf(c) != ControlKind.StickDirection).Select(c => new IndicatorViewModel(Names.Short(c))));
        InputAxes = CreateAxes();
        OutputAxes = CreateAxes();

        var placeholder = MappingConfig.CreateDefault();
        LeftStick = new StickPanelViewModel("Left stick", placeholder.LeftStick, OnConfigEdited);
        RightStick = new StickPanelViewModel("Right stick", placeholder.RightStick, OnConfigEdited);
        LeftTrigger = new TriggerPanelViewModel("Left trigger", placeholder.LeftTrigger, OnConfigEdited);
        RightTrigger = new TriggerPanelViewModel("Right trigger", placeholder.RightTrigger, OnConfigEdited);

        _profiles.PendingChanged += p => HasPendingSave = p;

        var loaded = _profiles.LoadAll(out var issues);
        Games = new ObservableCollection<GameItemViewModel>(loaded.Select(g => new GameItemViewModel(g)));
        if (issues.Count > 0) ShowToast($"{issues.Count} damaged profile file(s) were set aside (*.corrupt).", isError: true);

        RestoreAssignments();
        _remapEnabled = settings.Current.RemapEnabled;
        _remap.SetEnabled(_remapEnabled);

        SelectedGame = Games.FirstOrDefault(g => g.Model.Id == settings.Current.LastGameId) ?? Games.FirstOrDefault();

        _remap.DevicesChanged += list => Application.Current?.Dispatcher.BeginInvoke(() => SyncDevices(list));
        _remap.Problem += msg => Application.Current?.Dispatcher.BeginInvoke(() => { ShowToast(msg, isError: true); RefreshDriverBanners(); });
        SyncDevices(_remap.Devices);

        _liveTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _liveTimer.Tick += (_, _) => UpdateLive();
        _liveTimer.Start();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastText = null; };

        RefreshDriverBanners();
    }

    // ------------------------------------------------------------------ collections / state

    public SettingsViewModel Settings { get; }
    public HidHideViewModel HidHide { get; } = new();
    public ObservableCollection<GameItemViewModel> Games { get; }
    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];
    public ObservableCollection<SlotItemViewModel> Slots { get; }
    public ObservableCollection<LayerItemViewModel> Layers { get; }
    public ObservableCollection<IndicatorViewModel> InputIndicators { get; }
    public ObservableCollection<IndicatorViewModel> OutputIndicators { get; }
    public ObservableCollection<AxisViewModel> InputAxes { get; }
    public ObservableCollection<AxisViewModel> OutputAxes { get; }
    public ObservableCollection<string> GameExecutables { get; } = [];

    public StickPanelViewModel LeftStick { get; }
    public StickPanelViewModel RightStick { get; }
    public TriggerPanelViewModel LeftTrigger { get; }
    public TriggerPanelViewModel RightTrigger { get; }
    public GroupLabelViewModel LeftStickGroup { get; } = new();
    public GroupLabelViewModel RightStickGroup { get; } = new();
    public GroupLabelViewModel DPadGroup { get; } = new();

    public ControlBindingViewModel this[Control c] => _controls[c];

    public static IReadOnlyList<string> PageTitles { get; } = ["Mapping", "Sticks & triggers", "Live input", "Game & config"];

    [ObservableProperty] private GameItemViewModel? _selectedGame;
    [ObservableProperty] private ConfigTabViewModel? _selectedConfig;
    [ObservableProperty] private int _selectedLayer;
    [ObservableProperty] private int _pageIndex;
    [ObservableProperty] private DeviceItemViewModel? _selectedDevice;
    [ObservableProperty] private int _selectedSlot;
    [ObservableProperty] private bool _remapEnabled;
    [ObservableProperty] private BindingEditorViewModel? _editor;
    [ObservableProperty] private bool _isSidebarCollapsed;
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isAboutOpen;
    [ObservableProperty] private bool _isHidHideOpen;
    [ObservableProperty] private string? _toastText;
    [ObservableProperty] private bool _toastIsError;
    [ObservableProperty] private bool _hasPendingSave;
    [ObservableProperty] private bool _showViGEmBanner;
    [ObservableProperty] private bool _showHidHideBanner;
    [ObservableProperty] private string _viGEmBannerText = "";
    [ObservableProperty] private string _pollRateText = "";
    [ObservableProperty] private string _liveStatus = "No controller";
    [ObservableProperty] private string _newExecutable = "";

    public event Action<bool>? RemapStateChanged;

    public bool IsEditorOpen => Editor is not null;
    public bool HasDevice => SelectedDevice is not null;
    public bool HasMultipleDevices => Devices.Count > 1;
    public string PageTitle => PageTitles[Math.Clamp(PageIndex, 0, PageTitles.Count - 1)];
    public bool IsOverlayOpen => IsSettingsOpen || IsAboutOpen || IsHidHideOpen;
    public string AppVersion => AppInfo.Version;
    public bool HasRepository => AppInfo.RepositoryUrl.Length > 0;

    private MappingConfig? Config => SelectedConfig?.Model;

    // Config-level options (Game & config page)
    public VirtualOutput OutputMode
    {
        get => Config?.Output ?? VirtualOutput.Xbox360;
        set { if (Config is null || Config.Output == value) return; Config.Output = value; OnConfigEdited(); OnPropertyChanged(); }
    }

    public bool SwapSticks
    {
        get => Config?.SwapSticks ?? false;
        set { if (Config is null || Config.SwapSticks == value) return; Config.SwapSticks = value; OnConfigEdited(); OnPropertyChanged(); }
    }

    public bool ForwardRumble
    {
        get => Config?.ForwardRumble ?? true;
        set { if (Config is null || Config.ForwardRumble == value) return; Config.ForwardRumble = value; OnConfigEdited(); OnPropertyChanged(); }
    }

    // ------------------------------------------------------------------ selection changes

    partial void OnSelectedGameChanged(GameItemViewModel? value)
    {
        Editor = null;
        GameExecutables.Clear();
        if (value is null)
        {
            SelectedConfig = null;
            return;
        }
        foreach (var e in value.Model.Executables) GameExecutables.Add(e);
        var applied = AppliedAssignment();
        SelectedConfig = value.Configs.FirstOrDefault(c => applied?.ConfigId == c.Model.Id) ?? value.Configs.FirstOrDefault();
        _settings.Current.LastGameId = value.Model.Id;
        _settings.Save();
    }

    partial void OnSelectedConfigChanged(ConfigTabViewModel? value)
    {
        Editor = null;
        if (value is null) return;
        LeftStick.Rebind(value.Model.LeftStick);
        RightStick.Rebind(value.Model.RightStick);
        LeftTrigger.Rebind(value.Model.LeftTrigger);
        RightTrigger.Rebind(value.Model.RightTrigger);
        OnPropertyChanged(nameof(OutputMode));
        OnPropertyChanged(nameof(SwapSticks));
        OnPropertyChanged(nameof(ForwardRumble));
        RefreshBindings();
    }

    partial void OnSelectedLayerChanged(int value)
    {
        var src = Editor?.Source;
        RefreshBindings();
        Editor = src is { } s ? new BindingEditorViewModel(this, s) : null;
    }

    partial void OnPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PageTitle));
        if (value != 0) Editor = null;
    }

    partial void OnEditorChanged(BindingEditorViewModel? value)
    {
        foreach (var c in _controls.Values) c.IsSelected = value is not null && c.Control == value.Source;
        LeftStickGroup.IsSelected = value is not null && IsLeftStick(value.Source);
        RightStickGroup.IsSelected = value is not null && IsRightStick(value.Source);
        DPadGroup.IsSelected = value is not null && value.Source is >= Control.DPadUp and <= Control.DPadRight;
        OnPropertyChanged(nameof(IsEditorOpen));
    }

    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasDevice));
        if (value is not null)
        {
            _settings.Current.LastDeviceKey = value.Key;
            _settings.Save();
            SelectedSlot = value.ActiveSlot;
        }
        RefreshApplied();
    }

    partial void OnRemapEnabledChanged(bool value)
    {
        _remap.SetEnabled(value);
        _settings.Current.RemapEnabled = value;
        _settings.Save();
        RemapStateChanged?.Invoke(value);
        if (value) RefreshDriverBanners();
    }

    partial void OnIsSettingsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOverlayOpen));
        if (value) Settings.RefreshDrivers();
    }

    partial void OnIsAboutOpenChanged(bool value) => OnPropertyChanged(nameof(IsOverlayOpen));
    partial void OnIsHidHideOpenChanged(bool value) => OnPropertyChanged(nameof(IsOverlayOpen));

    // ------------------------------------------------------------------ IBindingHost

    public int Layer => SelectedLayer;

    public ButtonBinding? GetOwnBinding(Control source) =>
        Config is { } c && SelectedLayer < c.Layers.Count ? c.Layers[SelectedLayer].Find(source) : null;

    public ButtonBinding? GetEffectiveBinding(Control source) => Config?.Resolve(source, SelectedLayer);

    public void SetBinding(Control source, ButtonBinding? binding)
    {
        if (Config is null) return;
        var layer = Config.Layers[SelectedLayer];
        if (binding is null) layer.Remove(source);
        else layer.Set(binding);
        OnConfigEdited();
    }

    [RelayCommand]
    public void SelectControl(Control source)
    {
        if (Config is null) return;
        PageIndex = 0;
        IsSidebarCollapsed = false;
        Editor = new BindingEditorViewModel(this, source);
    }

    [RelayCommand]
    public void CloseEditor() => Editor = null;

    /// <summary>Called after any edit to the selected config: persist + hot-reload into the engine.</summary>
    private void OnConfigEdited()
    {
        if (SelectedGame is null || Config is null) return;
        _profiles.MarkDirty(SelectedGame.Model);
        _remap.UpdateConfig(Config);
        RefreshBindings();
    }

    // ------------------------------------------------------------------ labels

    private void RefreshBindings()
    {
        var cfg = Config;
        foreach (var vm in _controls.Values)
        {
            if (cfg is null)
            {
                vm.Label = vm.ShortName;
                vm.IsRemapped = vm.IsDisabled = vm.IsInherited = false;
                continue;
            }
            var own = cfg.Layers[SelectedLayer].Find(vm.Control);
            var eff = own ?? (SelectedLayer > 0 ? cfg.Layers[0].Find(vm.Control) : null);
            vm.IsInherited = own is null && eff is not null;
            vm.IsDisabled = eff is { IsDisabled: true };
            vm.IsRemapped = eff is not null && !eff.IsDisabled;
            vm.Label = eff is null
                ? (cfg.Output == VirtualOutput.None && ControlInfo.IsDigitalButton(vm.Control) ? "—" : vm.ShortName)
                : Names.Binding(eff);
        }

        UpdateGroup(LeftStickGroup, cfg?.LeftStick, Control.LeftStickClick, ControlInfo.DirectionsOf(Stick.Left), "LS");
        UpdateGroup(RightStickGroup, cfg?.RightStick, Control.RightStickClick, ControlInfo.DirectionsOf(Stick.Right), "RS");
        UpdateGroup(DPadGroup, null, Control.None, ControlInfo.DPad, "D-Pad");

        if (cfg is not null)
        {
            for (int i = 0; i < Layers.Count; i++) Layers[i].HasBindings = cfg.Layers[i].Bindings.Count > 0;
        }
    }

    private void UpdateGroup(GroupLabelViewModel g, StickSettings? stick, Control click, IReadOnlyList<Control> dirs, string fallback)
    {
        var lines = new List<string>();
        if (stick?.Mode == StickMode.Mouse) lines.Add("◎ Mouse");
        else if (stick?.Mode == StickMode.Disabled) lines.Add("◎ Disabled");
        if (click != Control.None && _controls[click] is { } cv && (cv.IsRemapped || cv.IsDisabled)) lines.Add("● " + cv.Label);
        string[] arrows = ["↑", "↓", "←", "→"];
        for (int i = 0; i < dirs.Count; i++)
        {
            var d = _controls[dirs[i]];
            if (d.IsRemapped || d.IsDisabled) lines.Add($"{arrows[i]} {d.Label}");
        }
        g.IsRemapped = lines.Count > 0;
        g.Text = lines.Count > 0 ? string.Join("\n", lines) : fallback;
    }

    private static bool IsLeftStick(Control c) => c is Control.LeftStickClick or >= Control.LeftStickUp and <= Control.LeftStickRight;
    private static bool IsRightStick(Control c) => c is Control.RightStickClick or >= Control.RightStickUp and <= Control.RightStickRight;

    // ------------------------------------------------------------------ live input

    private void UpdateLive()
    {
        PollRateText = _remap.MeasuredPollingRate > 0 ? $"{_remap.MeasuredPollingRate:0} Hz" : "";
        if (SelectedDevice is null || !_remap.TryGetLive(SelectedDevice.Key, out var live))
        {
            foreach (var c in _controls.Values) { c.IsLivePressed = false; c.IsOutputPressed = false; }
            LeftStickGroup.IsLivePressed = RightStickGroup.IsLivePressed = DPadGroup.IsLivePressed = false;
            LiveStatus = SelectedDevice is null ? "Plug in via USB or Bluetooth" : "Waiting for input…";
            return;
        }

        var raw = live.Raw;
        var output = live.Output;
        foreach (var c in _controls.Values)
        {
            c.IsLivePressed = LiveMath.IsLiveDown(c.Control, raw);
            c.IsOutputPressed = live.Remapping && LiveMath.IsLiveDown(c.Control, output);
        }
        LeftStickGroup.IsLivePressed = raw.IsDown(GamepadButtons.LeftStick) || (raw.LeftX * raw.LeftX) + (raw.LeftY * raw.LeftY) > 0.04f;
        RightStickGroup.IsLivePressed = raw.IsDown(GamepadButtons.RightStick) || (raw.RightX * raw.RightX) + (raw.RightY * raw.RightY) > 0.04f;
        DPadGroup.IsLivePressed = (raw.Buttons & (GamepadButtons.DPadUp | GamepadButtons.DPadDown | GamepadButtons.DPadLeft | GamepadButtons.DPadRight)) != 0;
        for (int i = 0; i < Layers.Count; i++) Layers[i].IsLive = live.Remapping && live.ActiveLayer == i && i > 0;

        LiveStatus = !live.Connected ? "Controller not responding"
            : live.Remapping ? $"Remapping · Slot {live.ActiveSlot + 1}" + (live.ActiveLayer > 0 ? $" · Shift {live.ActiveLayer}" : "")
            : RemapEnabled ? "Connected · no config applied to this slot" : "Connected · remap is off";

        if (PageIndex == 1)
        {
            LeftStick.UpdateLive(raw.LeftX, raw.LeftY);
            RightStick.UpdateLive(raw.RightX, raw.RightY);
            LeftTrigger.UpdateLive(raw.LeftTrigger);
            RightTrigger.UpdateLive(raw.RightTrigger);
        }
        else if (PageIndex == 2)
        {
            int i = 0;
            foreach (var c in ControlInfo.All.Where(c => ControlInfo.KindOf(c) != ControlKind.StickDirection))
                InputIndicators[i++].IsOn = LiveMath.IsLiveDown(c, raw);
            i = 0;
            foreach (var c in ControlInfo.VirtualPadTargets.Where(c => ControlInfo.KindOf(c) != ControlKind.StickDirection))
                OutputIndicators[i++].IsOn = live.Remapping && LiveMath.IsLiveDown(c, output);
            SetAxes(InputAxes, raw);
            SetAxes(OutputAxes, live.Remapping ? output : default);
        }
    }

    private static ObservableCollection<AxisViewModel> CreateAxes() =>
    [
        new("Left X", true), new("Left Y", true), new("Right X", true), new("Right Y", true), new("LT", false), new("RT", false),
    ];

    private static void SetAxes(ObservableCollection<AxisViewModel> axes, in GamepadState s)
    {
        axes[0].Value = Math.Round(s.LeftX, 2);
        axes[1].Value = Math.Round(s.LeftY, 2);
        axes[2].Value = Math.Round(s.RightX, 2);
        axes[3].Value = Math.Round(s.RightY, 2);
        axes[4].Value = Math.Round(s.LeftTrigger, 2);
        axes[5].Value = Math.Round(s.RightTrigger, 2);
    }

    // ------------------------------------------------------------------ devices, slots, apply

    private void SyncDevices(IReadOnlyList<DeviceSnapshot> list)
    {
        foreach (var gone in Devices.Where(d => !list.Any(s => s.Info.Key == d.Key)).ToList()) Devices.Remove(gone);
        foreach (var snap in list)
        {
            var vm = Devices.FirstOrDefault(d => d.Key == snap.Info.Key);
            if (vm is null)
            {
                vm = new DeviceItemViewModel(snap.Info);
                Devices.Add(vm);
            }
            var assignment = _settings.Current.Devices.Find(d => d.DeviceKey == snap.Info.Key);
            vm.Update(snap.Info, assignment?.Alias);
            vm.ActiveSlot = snap.ActiveSlot;
            if (vm == SelectedDevice && SelectedSlot != snap.ActiveSlot) SelectedSlot = snap.ActiveSlot;
            vm.VirtualActive = snap.VirtualPadActive;
            vm.VirtualSlot = snap.VirtualSlot;
            if (assignment is not null && assignment.ActiveSlot != snap.ActiveSlot && assignment.Slots[snap.ActiveSlot] is not null)
            {
                assignment.ActiveSlot = snap.ActiveSlot; // switched with the controller hotkey
                _settings.Save();
            }
        }

        if (SelectedDevice is null || !Devices.Contains(SelectedDevice))
            SelectedDevice = Devices.FirstOrDefault(d => d.Key == _settings.Current.LastDeviceKey) ?? Devices.FirstOrDefault();
        else
            RefreshApplied();

        OnPropertyChanged(nameof(HasMultipleDevices));
        RefreshDriverBanners();
    }

    private SlotAssignment? AppliedAssignment()
    {
        if (SelectedDevice is null) return null;
        var a = _settings.Current.Devices.Find(d => d.DeviceKey == SelectedDevice.Key);
        if (a is null) return null;
        a.Normalize();
        return a.Slots[Math.Clamp(SelectedDevice.ActiveSlot, 0, DeviceAssignment.SlotCount - 1)];
    }

    private void RefreshApplied()
    {
        var applied = AppliedAssignment();
        foreach (var g in Games)
        {
            g.IsApplied = applied is not null && g.Model.Id == applied.GameId;
            foreach (var c in g.Configs) c.IsApplied = g.IsApplied && c.Model.Id == applied!.ConfigId;
        }

        var dev = SelectedDevice is null ? null : _settings.Current.Devices.Find(d => d.DeviceKey == SelectedDevice.Key);
        dev?.Normalize();
        foreach (var s in Slots)
        {
            var a = dev?.Slots[s.Index];
            s.IsActive = SelectedDevice is not null && SelectedDevice.ActiveSlot == s.Index;
            s.Assigned = a is null ? "Empty" : Describe(a) ?? "Missing config";
        }
    }

    private string? Describe(SlotAssignment a)
    {
        var g = Games.FirstOrDefault(x => x.Model.Id == a.GameId);
        var c = g?.Model.FindConfig(a.ConfigId);
        return g is null || c is null ? null : $"{g.Name} · {c.Name}";
    }

    private void RestoreAssignments()
    {
        foreach (var dev in _settings.Current.Devices)
        {
            dev.Normalize();
            for (int i = 0; i < DeviceAssignment.SlotCount; i++)
            {
                if (dev.Slots[i] is not { } a) continue;
                var cfg = Games.FirstOrDefault(g => g.Model.Id == a.GameId)?.Model.FindConfig(a.ConfigId);
                if (cfg is null) { dev.Slots[i] = null; continue; }
                _remap.AssignSlot(dev.DeviceKey, i, cfg, activate: i == dev.ActiveSlot);
            }
        }
    }

    [RelayCommand]
    private void Apply()
    {
        if (SelectedGame is null || Config is null) return;
        if (SelectedDevice is null)
        {
            ShowToast("Connect a controller first, then press Apply.", isError: true);
            return;
        }
        ApplyTo(SelectedDevice.Key, SelectedSlot, SelectedGame.Model, Config);
        SelectedDevice.ActiveSlot = SelectedSlot;
        if (!RemapEnabled) RemapEnabled = true;
        RefreshApplied();
        ShowToast($"Applied “{Config.Name}” to {SelectedDevice.DisplayName}, slot {SelectedSlot + 1}");
        RefreshDriverBanners();
    }

    private void ApplyTo(string deviceKey, int slot, GameProfile game, MappingConfig config)
    {
        var dev = _settings.Current.GetOrCreateDevice(deviceKey);
        dev.Slots[slot] = new SlotAssignment { GameId = game.Id, ConfigId = config.Id };
        dev.ActiveSlot = slot;
        _settings.Save();
        _profiles.Flush();
        _remap.AssignSlot(deviceKey, slot, config, activate: true);
    }

    [RelayCommand]
    private void ClearSlot()
    {
        if (SelectedDevice is null) return;
        var dev = _settings.Current.GetOrCreateDevice(SelectedDevice.Key);
        dev.Slots[SelectedSlot] = null;
        _settings.Save();
        _remap.AssignSlot(SelectedDevice.Key, SelectedSlot, null, activate: false);
        RefreshApplied();
        ShowToast($"Slot {SelectedSlot + 1} cleared");
    }

    partial void OnSelectedSlotChanged(int value)
    {
        if (SelectedDevice is null) return;
        var dev = _settings.Current.Devices.Find(d => d.DeviceKey == SelectedDevice.Key);
        if (dev is null) return;
        dev.Normalize();
        if (dev.Slots[value] is not null && SelectedDevice.ActiveSlot != value)
        {
            // Choosing a populated slot activates it.
            dev.ActiveSlot = value;
            SelectedDevice.ActiveSlot = value;
            _settings.Save();
            _remap.SetActiveSlot(SelectedDevice.Key, value);
            RefreshApplied();
        }
    }

    [RelayCommand]
    private void RenameDevice()
    {
        if (SelectedDevice is null) return;
        var name = _dialogs.Prompt("Rename controller", "Give this controller a friendly name.", SelectedDevice.DisplayName);
        if (name is null) return;
        var dev = _settings.Current.GetOrCreateDevice(SelectedDevice.Key);
        dev.Alias = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        _settings.Save();
        SelectedDevice.Update(SelectedDevice.Info, dev.Alias);
    }

    /// <summary>Automatic profile switching when a linked game gains focus.</summary>
    public void OnForegroundChanged(string exePath)
    {
        if (!_settings.Current.AutoProfileSwitching || Devices.Count == 0) return;
        var game = Games.FirstOrDefault(g => g.Model.MatchesExecutable(exePath));
        if (game is null) return;
        var config = game.Model.Configs[0];
        bool changed = false;
        foreach (var d in Devices)
        {
            var a = _settings.Current.GetOrCreateDevice(d.Key);
            var current = a.Slots[a.ActiveSlot];
            if (current?.ConfigId == config.Id) continue;
            ApplyTo(d.Key, a.ActiveSlot, game.Model, config);
            changed = true;
        }
        if (!changed) return;
        Log.Information("Auto-applied {Game} for {Exe}", game.Name, Path.GetFileName(exePath));
        if (Editor is null) SelectedGame = game;
        RefreshApplied();
        ShowToast($"Auto-applied “{game.Name}”");
    }

    // ------------------------------------------------------------------ games & configs

    [RelayCommand]
    private void AddGame()
    {
        var name = _dialogs.Prompt("Add game", "Name of the game or application:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var p = GameProfile.Create(name.Trim());
        var vm = new GameItemViewModel(p);
        InsertSorted(vm);
        _profiles.MarkDirty(p);
        SelectedGame = vm;
        PageIndex = 3;
        ShowToast("Tip: link the game's .exe below to switch to it automatically.");
    }

    [RelayCommand]
    private void RenameGame(GameItemViewModel? game)
    {
        game ??= SelectedGame;
        if (game is null) return;
        var name = _dialogs.Prompt("Rename game", "New name:", game.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        game.Name = name.Trim();
        _profiles.MarkDirty(game.Model);
        RefreshApplied();
    }

    [RelayCommand]
    private void DuplicateGame(GameItemViewModel? game)
    {
        game ??= SelectedGame;
        if (game is null) return;
        var copy = ProfileStore.Deserialize(ProfileStore.Serialize(game.Model));
        copy.Id = Guid.NewGuid();
        copy.Name = game.Name + " (copy)";
        copy.Executables.Clear();
        foreach (var c in copy.Configs) c.Id = Guid.NewGuid();
        var vm = new GameItemViewModel(copy);
        InsertSorted(vm);
        _profiles.MarkDirty(copy);
        SelectedGame = vm;
    }

    [RelayCommand]
    private void DeleteGame(GameItemViewModel? game)
    {
        game ??= SelectedGame;
        if (game is null) return;
        if (!_dialogs.Confirm("Delete game", $"Delete “{game.Name}” and all of its configs? This cannot be undone.", "Delete", danger: true)) return;
        ClearAssignments(a => a.GameId == game.Model.Id);
        _profiles.Delete(game.Model);
        int idx = Games.IndexOf(game);
        Games.Remove(game);
        if (Games.Count == 0)
        {
            var p = GameProfile.Create("General");
            Games.Add(new GameItemViewModel(p));
            _profiles.MarkDirty(p);
        }
        SelectedGame = Games[Math.Clamp(idx, 0, Games.Count - 1)];
    }

    [RelayCommand]
    private void ExportGame(GameItemViewModel? game)
    {
        game ??= SelectedGame;
        if (game is null) return;
        var path = _dialogs.SaveFile(ProfileFilter, SafeFileName(game.Name) + ProfileStore.FileExtension);
        if (path is null) return;
        try
        {
            ProfileStore.Export(game.Model, path);
            ShowToast($"Exported to {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export failed");
            ShowToast("Export failed: " + ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private void ImportGame()
    {
        var path = _dialogs.OpenFile(ProfileFilter);
        if (path is null) return;
        try
        {
            var p = ProfileStore.Import(path);
            var vm = new GameItemViewModel(p);
            InsertSorted(vm);
            _profiles.MarkDirty(p);
            SelectedGame = vm;
            ShowToast($"Imported “{p.Name}”");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Import failed");
            ShowToast("This file could not be imported: " + ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private void AddConfig()
    {
        if (SelectedGame is null) return;
        var name = _dialogs.Prompt("New config", "Config name:", $"Config {SelectedGame.Configs.Count + 1}");
        if (string.IsNullOrWhiteSpace(name)) return;
        var cfg = MappingConfig.CreateDefault(name.Trim());
        SelectedGame.Model.Configs.Add(cfg);
        var vm = new ConfigTabViewModel(cfg);
        SelectedGame.Configs.Add(vm);
        _profiles.MarkDirty(SelectedGame.Model);
        SelectedConfig = vm;
    }

    [RelayCommand]
    private void RenameConfig(ConfigTabViewModel? config)
    {
        config ??= SelectedConfig;
        if (config is null || SelectedGame is null) return;
        var name = _dialogs.Prompt("Rename config", "New name:", config.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        config.Name = name.Trim();
        _profiles.MarkDirty(SelectedGame.Model);
        RefreshApplied();
    }

    [RelayCommand]
    private void DuplicateConfig(ConfigTabViewModel? config)
    {
        config ??= SelectedConfig;
        if (config is null || SelectedGame is null) return;
        var copy = config.Model.Clone(newId: true);
        copy.Name = config.Name + " (copy)";
        SelectedGame.Model.Configs.Add(copy);
        var vm = new ConfigTabViewModel(copy);
        SelectedGame.Configs.Add(vm);
        _profiles.MarkDirty(SelectedGame.Model);
        SelectedConfig = vm;
    }

    [RelayCommand]
    private void DeleteConfig(ConfigTabViewModel? config)
    {
        config ??= SelectedConfig;
        if (config is null || SelectedGame is null) return;
        if (SelectedGame.Configs.Count <= 1)
        {
            ShowToast("A game needs at least one config.", isError: true);
            return;
        }
        if (!_dialogs.Confirm("Delete config", $"Delete config “{config.Name}”?", "Delete", danger: true)) return;
        ClearAssignments(a => a.ConfigId == config.Model.Id);
        int idx = SelectedGame.Configs.IndexOf(config);
        SelectedGame.Model.Configs.Remove(config.Model);
        SelectedGame.Configs.Remove(config);
        _profiles.MarkDirty(SelectedGame.Model);
        SelectedConfig = SelectedGame.Configs[Math.Clamp(idx, 0, SelectedGame.Configs.Count - 1)];
    }

    [RelayCommand]
    private void ResetLayer()
    {
        if (Config is null) return;
        string layerName = SelectedLayer == 0 ? "main layer" : $"shift layer {SelectedLayer}";
        if (!_dialogs.Confirm("Reset layer", $"Remove every mapping from the {layerName} of “{Config.Name}”?", "Reset", danger: true)) return;
        Config.Layers[SelectedLayer].Bindings.Clear();
        Editor = null;
        OnConfigEdited();
    }

    [RelayCommand]
    private void AddExecutableFromFile()
    {
        if (SelectedGame is null) return;
        var path = _dialogs.OpenFile("Programs (*.exe)|*.exe");
        if (path is not null) AddExecutable(Path.GetFileName(path));
    }

    [RelayCommand]
    private void AddExecutableByName()
    {
        var name = NewExecutable.Trim();
        if (name.Length == 0) return;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        AddExecutable(name);
        NewExecutable = "";
    }

    private void AddExecutable(string fileName)
    {
        if (SelectedGame is null) return;
        if (SelectedGame.Model.Executables.Contains(fileName, StringComparer.OrdinalIgnoreCase)) return;
        var owner = Games.FirstOrDefault(g => g != SelectedGame && g.Model.MatchesExecutable(fileName));
        if (owner is not null) ShowToast($"{fileName} is also linked to “{owner.Name}”; the first match wins.", isError: true);
        SelectedGame.Model.Executables.Add(fileName);
        GameExecutables.Add(fileName);
        SelectedGame.RefreshExecutables();
        _profiles.MarkDirty(SelectedGame.Model);
    }

    [RelayCommand]
    private void RemoveExecutable(string exe)
    {
        if (SelectedGame is null) return;
        SelectedGame.Model.Executables.RemoveAll(e => string.Equals(e, exe, StringComparison.OrdinalIgnoreCase));
        GameExecutables.Remove(exe);
        SelectedGame.RefreshExecutables();
        _profiles.MarkDirty(SelectedGame.Model);
    }

    private void ClearAssignments(Func<SlotAssignment, bool> match)
    {
        foreach (var dev in _settings.Current.Devices)
        {
            dev.Normalize();
            for (int i = 0; i < dev.Slots.Count; i++)
            {
                if (dev.Slots[i] is { } a && match(a))
                {
                    dev.Slots[i] = null;
                    _remap.AssignSlot(dev.DeviceKey, i, null, activate: false);
                }
            }
        }
        _settings.Save();
        RefreshApplied();
    }

    private void InsertSorted(GameItemViewModel vm)
    {
        int i = 0;
        while (i < Games.Count && string.Compare(Games[i].Name, vm.Name, StringComparison.CurrentCultureIgnoreCase) < 0) i++;
        Games.Insert(i, vm);
    }

    private static string SafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // ------------------------------------------------------------------ chrome, overlays, toasts

    [RelayCommand] private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;
    [RelayCommand] private void OpenSettings() { IsAboutOpen = IsHidHideOpen = false; IsSettingsOpen = true; }
    [RelayCommand] private void OpenAbout() { IsSettingsOpen = IsHidHideOpen = false; IsAboutOpen = true; }

    [RelayCommand]
    private void OpenHidHide()
    {
        IsSettingsOpen = IsAboutOpen = false;
        IsHidHideOpen = true;
        if (HidHide.IsInstalled && !HidHide.IsLoaded) HidHide.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void CloseOverlays()
    {
        IsSettingsOpen = IsAboutOpen = IsHidHideOpen = false;
        RefreshDriverBanners();
    }

    [RelayCommand] private void SetPage(string index) => PageIndex = int.Parse(index, System.Globalization.CultureInfo.InvariantCulture);
    [RelayCommand] private void DismissHidHideBanner() { _hidHideBannerDismissedOnce = true; ShowHidHideBanner = false; }
    [RelayCommand] private static void OpenRepository() { if (AppInfo.RepositoryUrl.Length > 0) SettingsViewModel.Open(AppInfo.RepositoryUrl); }
    [RelayCommand] private static void InstallViGEm() => SettingsViewModel.Open(DriverInfo.ViGEmBusDownloadUrl);
    [RelayCommand] private static void InstallHidHide() => SettingsViewModel.Open(DriverInfo.HidHideDownloadUrl);
    [RelayCommand] private void DismissToast() { _toastTimer.Stop(); ToastText = null; }

    public void ShowToast(string text, bool isError = false)
    {
        ToastIsError = isError;
        ToastText = text;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void RefreshDriverBanners()
    {
        bool usesVirtual = Config?.Output != VirtualOutput.None;
        ShowViGEmBanner = usesVirtual && (!DriverInfo.ViGEmBus().Installed || _remap.VirtualBusStatus is VirtualBusStatus.NotInstalled or VirtualBusStatus.VersionMismatch);
        ViGEmBannerText = _remap.VirtualBusDetail ?? "The ViGEmBus driver is missing, so no virtual controller can be created. Install it, then restart reController.";
        ShowHidHideBanner = usesVirtual && RemapEnabled && !DriverInfo.HidHide().Installed && !_hidHideBannerDismissedOnce;
    }

    private bool _hidHideBannerDismissedOnce;

    /// <summary>Live input visualisation only runs while the window is visible.</summary>
    public void SetUiActive(bool active)
    {
        if (active) _liveTimer.Start();
        else _liveTimer.Stop();
    }

    public void Shutdown()
    {
        _liveTimer.Stop();
        _profiles.Flush();
        _settings.SaveNow();
    }
}
