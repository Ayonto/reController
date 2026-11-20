using CommunityToolkit.Mvvm.ComponentModel;
using ReController.Core.Input;
using ReController.Core.Mapping;

namespace ReController.App.ViewModels;

/// <summary>Tuning + live preview for one thumbstick.</summary>
public sealed partial class StickPanelViewModel : ObservableObject
{
    public const double PreviewSize = 150;

    private readonly Action _changed;
    private StickSettings _s;

    public StickPanelViewModel(string title, StickSettings settings, Action changed)
    {
        Title = title;
        _s = settings;
        _changed = changed;
    }

    public string Title { get; }

    public IReadOnlyList<StickMode> Modes { get; } = Enum.GetValues<StickMode>();

    public void Rebind(StickSettings settings)
    {
        _s = settings;
        OnPropertyChanged(string.Empty);
    }

    public StickMode Mode { get => _s.Mode; set { if (_s.Mode == value) return; _s.Mode = value; Changed(); OnPropertyChanged(nameof(IsMouse)); } }
    public double Deadzone { get => _s.Deadzone; set => Set(v => _s.Deadzone = v, Math.Clamp(value, 0, 0.9), _s.Deadzone, nameof(DeadzoneDiameter)); }
    public double OuterDeadzone { get => _s.OuterDeadzone; set => Set(v => _s.OuterDeadzone = v, Math.Clamp(value, 0.1, 1), _s.OuterDeadzone, nameof(OuterDiameter)); }
    public double AntiDeadzone { get => _s.AntiDeadzone; set => Set(v => _s.AntiDeadzone = v, Math.Clamp(value, 0, 0.9), _s.AntiDeadzone); }
    public double Curve { get => _s.Curve; set => Set(v => _s.Curve = v, Math.Clamp(value, 0.2, 5), _s.Curve); }
    public double MouseSpeed { get => _s.MouseSpeed; set => Set(v => _s.MouseSpeed = v, Math.Clamp(value, 100, 6000), _s.MouseSpeed); }
    public double DirectionThreshold { get => _s.DirectionThreshold; set => Set(v => _s.DirectionThreshold = v, Math.Clamp(value, 0.1, 0.95), _s.DirectionThreshold); }
    public bool InvertX { get => _s.InvertX; set { if (_s.InvertX == value) return; _s.InvertX = value; Changed(); } }
    public bool InvertY { get => _s.InvertY; set { if (_s.InvertY == value) return; _s.InvertY = value; Changed(); } }

    public bool IsMouse => Mode == StickMode.Mouse;
    public double DeadzoneDiameter => Deadzone * PreviewSize;
    public double OuterDiameter => OuterDeadzone * PreviewSize;

    // Live preview (canvas coordinates, top-left of a 10px dot).
    [ObservableProperty] private double _rawLeft = (PreviewSize / 2) - 5;
    [ObservableProperty] private double _rawTop = (PreviewSize / 2) - 5;
    [ObservableProperty] private double _outLeft = (PreviewSize / 2) - 5;
    [ObservableProperty] private double _outTop = (PreviewSize / 2) - 5;
    [ObservableProperty] private string _readout = "0.00, 0.00";

    public void UpdateLive(float x, float y)
    {
        double r = PreviewSize / 2;
        RawLeft = r + (x * r) - 5;
        RawTop = r - (y * r) - 5;
        var (ox, oy) = AnalogProcessing.ShapeStick(x, y, _s);
        OutLeft = r + (ox * r) - 5;
        OutTop = r - (oy * r) - 5;
        Readout = $"{ox,5:0.00}, {oy,5:0.00}";
    }

    private void Set(Action<double> apply, double value, double current, string? extra = null, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (Math.Abs(value - current) < 1e-9) return;
        apply(value);
        OnPropertyChanged(name);
        if (extra is not null) OnPropertyChanged(extra);
        _changed();
    }

    private void Changed() => _changed();
}

/// <summary>Tuning + live preview for one trigger.</summary>
public sealed partial class TriggerPanelViewModel(string title, TriggerSettings settings, Action changed) : ObservableObject
{
    private TriggerSettings _s = settings;

    public string Title { get; } = title;

    public void Rebind(TriggerSettings s)
    {
        _s = s;
        OnPropertyChanged(string.Empty);
    }

    public double Deadzone { get => _s.Deadzone; set { value = Math.Clamp(value, 0, 0.9); if (Math.Abs(_s.Deadzone - value) < 1e-9) return; _s.Deadzone = value; OnPropertyChanged(); changed(); } }
    public double MaxZone { get => _s.MaxZone; set { value = Math.Clamp(value, 0.1, 1); if (Math.Abs(_s.MaxZone - value) < 1e-9) return; _s.MaxZone = value; OnPropertyChanged(); changed(); } }
    public double DigitalThreshold { get => _s.DigitalThreshold; set { value = Math.Clamp(value, 0.02, 1); if (Math.Abs(_s.DigitalThreshold - value) < 1e-9) return; _s.DigitalThreshold = value; OnPropertyChanged(); OnPropertyChanged(nameof(ThresholdLeft)); changed(); } }
    public bool HairTrigger { get => _s.HairTrigger; set { if (_s.HairTrigger == value) return; _s.HairTrigger = value; OnPropertyChanged(); changed(); } }

    public const double BarWidth = 220;

    [ObservableProperty] private double _raw;
    [ObservableProperty] private double _output;

    public double RawBar => Raw * BarWidth;
    public double OutputBar => Output * BarWidth;
    public double ThresholdLeft => DigitalThreshold * BarWidth;

    partial void OnRawChanged(double value) => OnPropertyChanged(nameof(RawBar));
    partial void OnOutputChanged(double value) => OnPropertyChanged(nameof(OutputBar));

    public void UpdateLive(float raw)
    {
        Raw = raw;
        Output = AnalogProcessing.ShapeTrigger(raw, _s);
    }
}

public static class LiveMath
{
    public static bool IsLiveDown(Control c, in GamepadState s) => ControlInfo.KindOf(c) switch
    {
        ControlKind.Trigger => (c == Control.LeftTrigger ? s.LeftTrigger : s.RightTrigger) > 0.35f,
        ControlKind.StickDirection => Along(c, s) > 0.5f,
        _ => s.IsDown(ControlInfo.ToButtonFlag(c)),
    };

    private static float Along(Control c, in GamepadState s)
    {
        var (stick, dx, dy) = ControlInfo.StickDirection(c);
        var (x, y) = s.GetStick(stick);
        return (x * dx) + (y * dy);
    }
}
