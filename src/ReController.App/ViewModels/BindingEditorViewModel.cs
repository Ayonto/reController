using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReController.Core.Input;
using ReController.Core.Mapping;
using ReController.Core.Text;

namespace ReController.App.ViewModels;

public enum EditorKind
{
    Default,
    Gamepad,
    Keyboard,
    Mouse,
    Shift,
    Disabled,
}

public interface IBindingHost
{
    int Layer { get; }
    ButtonBinding? GetOwnBinding(Control source);
    ButtonBinding? GetEffectiveBinding(Control source);
    void SetBinding(Control source, ButtonBinding? binding);
    void SelectControl(Control source);
    void CloseEditor();
}

public sealed partial class TargetChipViewModel(Control control) : ObservableObject
{
    public Control Control { get; } = control;
    public string Name { get; } = Names.Short(control);
    public string ToolTip { get; } = Names.Long(control);
    [ObservableProperty] private bool _isChecked;
}

public sealed partial class SourceChipViewModel(Control control, string name) : ObservableObject
{
    public Control Control { get; } = control;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isSelected;
}

public sealed record KeyChip(ushort VirtualKey, string Name);

public sealed record MouseOption(string Name, MappingAction Action);

/// <summary>Edits the binding of one source control on the current layer. Every change is committed immediately.</summary>
public sealed partial class BindingEditorViewModel : ObservableObject
{
    public const int MaxKeys = 4;

    private readonly IBindingHost _host;
    private bool _loading;

    public BindingEditorViewModel(IBindingHost host, Control source)
    {
        _host = host;
        Source = source;
        Title = Names.Long(source);
        LayerName = host.Layer == 0 ? "Main layer" : $"Shift layer {host.Layer}";
        SourceChips = new ObservableCollection<SourceChipViewModel>(SiblingsOf(source).Select(c => new SourceChipViewModel(c.Control, c.Name) { IsSelected = c.Control == source }));
        GamepadTargets = new ObservableCollection<TargetChipViewModel>(ControlInfo.VirtualPadTargets.Select(c => new TargetChipViewModel(c)));
        foreach (var t in GamepadTargets) t.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TargetChipViewModel.IsChecked)) Commit(); };
        Load();
    }

    public Control Source { get; }
    public string Title { get; }
    public string LayerName { get; }
    public bool CanShift => _host.Layer == 0;
    public bool IsShiftLayer => _host.Layer > 0;
    public ObservableCollection<SourceChipViewModel> SourceChips { get; }
    public bool HasSourceChips => SourceChips.Count > 1;
    public ObservableCollection<TargetChipViewModel> GamepadTargets { get; }
    public ObservableCollection<KeyChip> Keys { get; } = [];

    public IReadOnlyList<MouseOption> MouseOptions { get; } =
    [
        new("Left click", new MouseButtonAction(MouseButton.Left)),
        new("Right click", new MouseButtonAction(MouseButton.Right)),
        new("Middle click", new MouseButtonAction(MouseButton.Middle)),
        new("Mouse 4 (back)", new MouseButtonAction(MouseButton.X1)),
        new("Mouse 5 (forward)", new MouseButtonAction(MouseButton.X2)),
        new("Wheel up", new MouseWheelAction(WheelDirection.Up)),
        new("Wheel down", new MouseWheelAction(WheelDirection.Down)),
        new("Wheel left", new MouseWheelAction(WheelDirection.Left)),
        new("Wheel right", new MouseWheelAction(WheelDirection.Right)),
    ];

    public IReadOnlyList<int> ShiftLayers { get; } = [1, 2, 3];

    [ObservableProperty] private EditorKind _kind;
    [ObservableProperty] private MouseOption? _selectedMouse;
    [ObservableProperty] private int _shiftLayer = 1;
    [ObservableProperty] private ActivationMode _mode;
    [ObservableProperty] private double _turboRate = 10;
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _summary = "";

    public bool ShowActivation => Kind is EditorKind.Gamepad or EditorKind.Keyboard or EditorKind.Mouse or EditorKind.Shift;
    public bool CanTurbo => Kind != EditorKind.Shift;
    public bool ShowTurbo => Kind != EditorKind.Shift && Mode is ActivationMode.Turbo or ActivationMode.ToggleTurbo;
    public string KeysPrompt => IsCapturing ? "Press up to 4 keys…" : Keys.Count == 0 ? "Click here, then press a key" : "Click to record again";

    private void Load()
    {
        _loading = true;
        try
        {
            var own = _host.GetOwnBinding(Source);
            var effective = own ?? _host.GetEffectiveBinding(Source);
            foreach (var t in GamepadTargets) t.IsChecked = false;
            Keys.Clear();
            SelectedMouse = null;
            Mode = own?.Mode ?? ActivationMode.Hold;
            TurboRate = own?.TurboRateHz ?? 10;

            if (own is null)
            {
                Kind = EditorKind.Default;
            }
            else if (own.IsDisabled)
            {
                Kind = EditorKind.Disabled;
            }
            else
            {
                var first = own.Actions[0];
                switch (first)
                {
                    case GamepadAction:
                        Kind = EditorKind.Gamepad;
                        foreach (var g in own.Actions.OfType<GamepadAction>())
                            if (GamepadTargets.FirstOrDefault(t => t.Control == g.Target) is { } chip) chip.IsChecked = true;
                        break;
                    case KeyAction:
                        Kind = EditorKind.Keyboard;
                        foreach (var k in own.Actions.OfType<KeyAction>()) Keys.Add(new KeyChip(k.VirtualKey, Names.Key(k.VirtualKey)));
                        break;
                    case MouseButtonAction or MouseWheelAction:
                        Kind = EditorKind.Mouse;
                        SelectedMouse = MouseOptions.FirstOrDefault(o => o.Action == first);
                        break;
                    case ShiftAction s:
                        Kind = EditorKind.Shift;
                        ShiftLayer = s.Layer;
                        break;
                }
            }
            UpdateSummary(effective, own is null);
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnKindChanged(EditorKind value)
    {
        OnPropertyChanged(nameof(ShowActivation));
        OnPropertyChanged(nameof(ShowTurbo));
        OnPropertyChanged(nameof(CanTurbo));
        if (_loading) return;

        switch (value)
        {
            case EditorKind.Gamepad when !GamepadTargets.Any(t => t.IsChecked):
                _loading = true;
                var self = GamepadTargets.FirstOrDefault(t => t.Control == Source) ?? GamepadTargets[0];
                self.IsChecked = true;
                _loading = false;
                break;
            case EditorKind.Keyboard when Keys.Count == 0:
                IsCapturing = true;
                return; // commit once a key is captured
            case EditorKind.Mouse when SelectedMouse is null:
                _loading = true;
                SelectedMouse = MouseOptions[0];
                _loading = false;
                break;
            case EditorKind.Shift:
                if (Mode is ActivationMode.Turbo or ActivationMode.ToggleTurbo) Mode = ActivationMode.Hold;
                break;
        }
        Commit();
    }

    partial void OnSelectedMouseChanged(MouseOption? value) => Commit();
    partial void OnShiftLayerChanged(int value) => Commit();
    partial void OnTurboRateChanged(double value) => Commit();

    partial void OnModeChanged(ActivationMode value)
    {
        OnPropertyChanged(nameof(ShowTurbo));
        Commit();
    }

    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(KeysPrompt));

    /// <summary>Called by the view's key capture box.</summary>
    public void CaptureKey(ushort vk)
    {
        if (!IsCapturing || vk == 0) return;
        if (Keys.Count >= MaxKeys || Keys.Any(k => k.VirtualKey == vk)) return;
        Keys.Add(new KeyChip(vk, Names.Key(vk)));
        OnPropertyChanged(nameof(KeysPrompt));
        Commit();
    }

    [RelayCommand]
    private void StartCapture()
    {
        Keys.Clear();
        IsCapturing = true;
    }

    [RelayCommand]
    private void RemoveKey(KeyChip key)
    {
        Keys.Remove(key);
        OnPropertyChanged(nameof(KeysPrompt));
        Commit();
    }

    [RelayCommand]
    private void ResetToDefault() => Kind = EditorKind.Default;

    [RelayCommand]
    private void SelectSource(SourceChipViewModel chip) => _host.SelectControl(chip.Control);

    [RelayCommand]
    private void Close() => _host.CloseEditor();

    private void Commit()
    {
        if (_loading) return;
        ButtonBinding? binding = Kind switch
        {
            EditorKind.Default => null,
            EditorKind.Disabled => new ButtonBinding { Source = Source },
            EditorKind.Gamepad => Build(GamepadTargets.Where(t => t.IsChecked).Select(t => (MappingAction)new GamepadAction(t.Control))),
            EditorKind.Keyboard => Build(Keys.Select(k => (MappingAction)new KeyAction(k.VirtualKey))),
            EditorKind.Mouse => SelectedMouse is null ? null : Build([SelectedMouse.Action]),
            EditorKind.Shift => Build([new ShiftAction(Math.Clamp(ShiftLayer, 1, MappingConfig.MaxShiftLayers))]),
            _ => null,
        };

        // An "active" kind with nothing chosen yet keeps the previous binding instead of disabling the control.
        if (binding is { IsDisabled: true } && Kind != EditorKind.Disabled) return;
        if (binding is null && Kind != EditorKind.Default) return;

        _host.SetBinding(Source, binding);
        UpdateSummary(binding ?? _host.GetEffectiveBinding(Source), binding is null);
    }

    private ButtonBinding Build(IEnumerable<MappingAction> actions) => new()
    {
        Source = Source,
        Actions = actions.ToList(),
        Mode = Mode,
        TurboRateHz = TurboRate,
    };

    private void UpdateSummary(ButtonBinding? b, bool isDefault)
    {
        if (isDefault && b is null) Summary = IsShiftLayer ? "Inherits main layer (unchanged)" : $"Unchanged — outputs {Names.Short(Source)}";
        else if (isDefault && b is not null) Summary = $"Inherited from main: {Names.Binding(b)}";
        else Summary = b is null ? "" : Names.Binding(b);
    }

    private static IEnumerable<(Control Control, string Name)> SiblingsOf(Control c)
    {
        if (c is Control.LeftStickClick or >= Control.LeftStickUp and <= Control.LeftStickRight)
            return [(Control.LeftStickClick, "Click"), (Control.LeftStickUp, "↑ Up"), (Control.LeftStickDown, "↓ Down"), (Control.LeftStickLeft, "← Left"), (Control.LeftStickRight, "→ Right")];
        if (c is Control.RightStickClick or >= Control.RightStickUp and <= Control.RightStickRight)
            return [(Control.RightStickClick, "Click"), (Control.RightStickUp, "↑ Up"), (Control.RightStickDown, "↓ Down"), (Control.RightStickLeft, "← Left"), (Control.RightStickRight, "→ Right")];
        if (c is >= Control.DPadUp and <= Control.DPadRight)
            return [(Control.DPadUp, "↑ Up"), (Control.DPadDown, "↓ Down"), (Control.DPadLeft, "← Left"), (Control.DPadRight, "→ Right")];
        return [(c, Names.Short(c))];
    }
}
