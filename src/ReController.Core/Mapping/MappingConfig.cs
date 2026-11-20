using System.Text.Json.Serialization;
using ReController.Core.Input;

namespace ReController.Core.Mapping;

public enum ActivationMode
{
    /// <summary>Output is active exactly while the source is held.</summary>
    Hold,
    /// <summary>Each press flips the output on/off.</summary>
    Toggle,
    /// <summary>Output pulses at <see cref="ButtonBinding.TurboRateHz"/> while held.</summary>
    Turbo,
    /// <summary>Each press flips a turbo pulse on/off.</summary>
    ToggleTurbo,
}

public enum VirtualOutput
{
    /// <summary>Emulate an Xbox 360 controller via ViGEmBus.</summary>
    Xbox360,
    /// <summary>No virtual controller: only keyboard / mouse output is produced.</summary>
    None,
}

public enum StickMode
{
    Gamepad,
    Mouse,
    Disabled,
}

/// <summary>Maps one source control to zero or more actions. Zero actions = control disabled.</summary>
public sealed class ButtonBinding
{
    public Control Source { get; set; }
    public List<MappingAction> Actions { get; set; } = [];
    public ActivationMode Mode { get; set; } = ActivationMode.Hold;

    /// <summary>Presses per second for turbo modes.</summary>
    public double TurboRateHz { get; set; } = 10;

    [JsonIgnore]
    public bool IsDisabled => Actions.Count == 0;

    public ButtonBinding Clone() => new()
    {
        Source = Source,
        Actions = [.. Actions],
        Mode = Mode,
        TurboRateHz = TurboRateHz,
    };
}

public sealed class Layer
{
    public string Name { get; set; } = "Main";
    public List<ButtonBinding> Bindings { get; set; } = [];

    public ButtonBinding? Find(Control source) => Bindings.Find(b => b.Source == source);

    public void Set(ButtonBinding binding)
    {
        Bindings.RemoveAll(b => b.Source == binding.Source);
        Bindings.Add(binding);
    }

    public bool Remove(Control source) => Bindings.RemoveAll(b => b.Source == source) > 0;

    public Layer Clone() => new() { Name = Name, Bindings = Bindings.Select(b => b.Clone()).ToList() };
}

public sealed class StickSettings
{
    public StickMode Mode { get; set; } = StickMode.Gamepad;

    /// <summary>Radial inner deadzone, 0..0.9.</summary>
    public double Deadzone { get; set; } = 0.08;

    /// <summary>Magnitude at which output saturates, 0.1..1.</summary>
    public double OuterDeadzone { get; set; } = 1.0;

    /// <summary>Minimum output once outside the deadzone (counters a game's own deadzone), 0..0.9.</summary>
    public double AntiDeadzone { get; set; }

    /// <summary>Response curve exponent. 1 = linear, &gt;1 = finer control near centre.</summary>
    public double Curve { get; set; } = 1.0;

    public bool InvertX { get; set; }
    public bool InvertY { get; set; }

    /// <summary>Mouse mode: top cursor speed in pixels per second at full deflection.</summary>
    public double MouseSpeed { get; set; } = 1400;

    /// <summary>Threshold at which a stick direction counts as "pressed" for direction bindings.</summary>
    public double DirectionThreshold { get; set; } = 0.5;

    public StickSettings Clone() => (StickSettings)MemberwiseClone();
}

public sealed class TriggerSettings
{
    public double Deadzone { get; set; } = 0.02;

    /// <summary>Raw value treated as fully pulled.</summary>
    public double MaxZone { get; set; } = 1.0;

    /// <summary>Raw value at which the trigger counts as "pressed" for bindings.</summary>
    public double DigitalThreshold { get; set; } = 0.35;

    /// <summary>Any pull past the deadzone outputs a full pull.</summary>
    public bool HairTrigger { get; set; }

    public TriggerSettings Clone() => (TriggerSettings)MemberwiseClone();
}

/// <summary>
/// A complete remapping configuration ("config"). A game profile holds several of these
/// and any one of them can be applied to a controller slot.
/// </summary>
public sealed class MappingConfig
{
    public const int MaxShiftLayers = 3;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default";
    public VirtualOutput Output { get; set; } = VirtualOutput.Xbox360;

    /// <summary>Layers[0] is the main layer; Layers[1..3] are shift layers that inherit from main.</summary>
    public List<Layer> Layers { get; set; } = [];

    public StickSettings LeftStick { get; set; } = new();
    public StickSettings RightStick { get; set; } = new();
    public TriggerSettings LeftTrigger { get; set; } = new();
    public TriggerSettings RightTrigger { get; set; } = new();
    public bool SwapSticks { get; set; }

    /// <summary>Forward rumble from the game to the physical controller.</summary>
    public bool ForwardRumble { get; set; } = true;

    public static MappingConfig CreateDefault(string name = "Default")
    {
        var c = new MappingConfig { Name = name };
        c.Normalize();
        return c;
    }

    /// <summary>Repairs structural invariants after deserialisation or edits.</summary>
    public void Normalize()
    {
        Layers ??= [];
        if (Layers.Count == 0) Layers.Add(new Layer { Name = "Main" });
        while (Layers.Count < MaxShiftLayers + 1) Layers.Add(new Layer { Name = $"Shift {Layers.Count}" });
        if (Layers.Count > MaxShiftLayers + 1) Layers.RemoveRange(MaxShiftLayers + 1, Layers.Count - MaxShiftLayers - 1);
        foreach (var layer in Layers)
        {
            layer.Bindings ??= [];
            layer.Bindings.RemoveAll(b => b is null || b.Source == Control.None);
            // Last write wins on duplicates.
            layer.Bindings = layer.Bindings.GroupBy(b => b.Source).Select(g => g.Last()).ToList();
            foreach (var b in layer.Bindings)
            {
                b.Actions ??= [];
                b.Actions.RemoveAll(a => a is null || (a is ShiftAction s && (s.Layer < 1 || s.Layer > MaxShiftLayers)));
                b.TurboRateHz = Math.Clamp(double.IsFinite(b.TurboRateHz) ? b.TurboRateHz : 10, 1, 50);
            }
        }
        LeftStick ??= new();
        RightStick ??= new();
        LeftTrigger ??= new();
        RightTrigger ??= new();
        if (string.IsNullOrWhiteSpace(Name)) Name = "Default";
    }

    public MappingConfig Clone(bool newId = false) => new()
    {
        Id = newId ? Guid.NewGuid() : Id,
        Name = Name,
        Output = Output,
        Layers = Layers.Select(l => l.Clone()).ToList(),
        LeftStick = LeftStick.Clone(),
        RightStick = RightStick.Clone(),
        LeftTrigger = LeftTrigger.Clone(),
        RightTrigger = RightTrigger.Clone(),
        SwapSticks = SwapSticks,
        ForwardRumble = ForwardRumble,
    };

    public StickSettings StickSettingsFor(Stick s) => s == Stick.Left ? LeftStick : RightStick;
    public TriggerSettings TriggerSettingsFor(Control trigger) => trigger == Control.LeftTrigger ? LeftTrigger : RightTrigger;

    /// <summary>Resolves the binding for a source on a layer, falling back to the main layer.</summary>
    public ButtonBinding? Resolve(Control source, int layer)
    {
        if (layer > 0 && layer < Layers.Count && Layers[layer].Find(source) is { } shifted) return shifted;
        return Layers.Count > 0 ? Layers[0].Find(source) : null;
    }
}
