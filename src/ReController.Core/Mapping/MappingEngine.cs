using ReController.Core.Input;

namespace ReController.Core.Mapping;

/// <summary>
/// Deterministic, allocation-free (after warm-up) remapping state machine.
/// Feed it normalised physical input every tick and it produces the desired output frame.
/// </summary>
/// <remarks>
/// Not thread-safe: own one engine per controller and drive it from a single thread.
/// A binding is captured when its source is pressed and released with that same binding,
/// so switching layers while holding a button can never leave an output stuck.
/// </remarks>
public sealed class MappingEngine
{
    private const float Hysteresis = 0.05f;
    private const double WheelRepeatDelay = 0.35;
    private const double WheelRepeatInterval = 0.08;

    private static readonly int ControlCount = Enum.GetValues<Control>().Length;

    private readonly MappingConfig _config;
    private readonly SourceState[] _state;
    private double _lastTime = double.NaN;
    private int _layer;
    private long _pressSequence;

    public MappingEngine(MappingConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Normalize();
        _state = new SourceState[ControlCount];
    }

    public MappingConfig Config => _config;

    /// <summary>The shift layer currently in effect (0 = main).</summary>
    public int ActiveLayer => _layer;

    public void Reset()
    {
        Array.Clear(_state);
        _layer = 0;
        _lastTime = double.NaN;
    }

    /// <param name="input">Normalised physical input.</param>
    /// <param name="now">Monotonic time in seconds.</param>
    /// <param name="frame">Frame to fill; it is cleared first.</param>
    public void Process(in GamepadState input, double now, OutputFrame frame)
    {
        frame.Clear();
        double dt = double.IsNaN(_lastTime) ? 0 : Math.Clamp(now - _lastTime, 0, 0.05);
        _lastTime = now;

        // 1. Shape analog input once.
        var (lx, ly) = AnalogProcessing.ShapeStick(input.LeftX, input.LeftY, _config.LeftStick);
        var (rx, ry) = AnalogProcessing.ShapeStick(input.RightX, input.RightY, _config.RightStick);
        float lt = AnalogProcessing.ShapeTrigger(input.LeftTrigger, _config.LeftTrigger);
        float rt = AnalogProcessing.ShapeTrigger(input.RightTrigger, _config.RightTrigger);

        // 2. Update digital "down" for every source and handle press/release edges.
        //    Two passes so a shift key pressed on the same tick as another button takes effect first.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 1; i < ControlCount; i++)
            {
                var c = (Control)i;
                ref var st = ref _state[i];
                bool down = IsSourceDown(c, in input, lx, ly, rx, ry, st.Down);
                if (down == st.Down) continue;

                if (down)
                {
                    // A latched toggle is switched off by the next press, using the binding that latched it.
                    var binding = st.Toggled ? st.Binding : _config.Resolve(c, _layer);
                    bool isShift = binding is not null && HasShift(binding);
                    if ((pass == 0) != isShift) continue; // shift sources first, then the rest

                    st.Down = true;
                    st.PressTime = now;
                    st.Sequence = ++_pressSequence;
                    st.Binding = binding;
                    if (st.Toggled)
                    {
                        st.Toggled = false;
                    }
                    else if (binding is not null && binding.Mode is ActivationMode.Toggle or ActivationMode.ToggleTurbo)
                    {
                        st.Toggled = true;
                        st.ToggleTime = now;
                    }
                    if (isShift) _layer = ComputeLayer();
                }
                else
                {
                    bool isShift = st.Binding is not null && HasShift(st.Binding);
                    if ((pass == 0) != isShift) continue;
                    st.Down = false; // captured binding is dropped in step 4 once fully inactive
                    if (isShift) _layer = ComputeLayer();
                }
            }
        }
        _layer = ComputeLayer();
        frame.ActiveLayer = _layer;

        // 3. Analog passthrough (sticks, triggers) unless remapped or redirected.
        ref var pad = ref frame.Pad;
        RouteStick(Stick.Left, lx, ly, dt, frame);
        RouteStick(Stick.Right, rx, ry, dt, frame);
        if (_config.Resolve(Control.LeftTrigger, _layer) is null) pad.LeftTrigger = lt;
        if (_config.Resolve(Control.RightTrigger, _layer) is null) pad.RightTrigger = rt;

        // 4. Digital outputs from every source.
        for (int i = 1; i < ControlCount; i++)
        {
            var c = (Control)i;
            ref var st = ref _state[i];
            var binding = st.Binding;

            if (binding is null)
            {
                // Unbound digital button: straight passthrough while held.
                if (st.Down && ControlInfo.IsDigitalButton(c)) ApplyGamepadTarget(c, ref frame.Pad);
                continue;
            }

            bool active = binding.Mode switch
            {
                ActivationMode.Hold => st.Down,
                ActivationMode.Toggle => st.Toggled,
                ActivationMode.Turbo => st.Down && (HasShift(binding) || Pulse(now - st.PressTime, binding.TurboRateHz)),
                ActivationMode.ToggleTurbo => st.Toggled && (HasShift(binding) || Pulse(now - st.ToggleTime, binding.TurboRateHz)),
                _ => st.Down,
            };

            bool rising = active && !st.WasActive;
            st.WasActive = active;
            if (active)
            {
                if (rising) st.ActiveSince = now;
                foreach (var action in binding.Actions) Emit(action, ref st, now, frame, rising);
            }

            // Fully released and no longer latched: drop the captured binding.
            if (!st.Down && !active && !st.Toggled) st.Binding = null;
        }
    }

    private void RouteStick(Stick physical, float x, float y, double dt, OutputFrame frame)
    {
        var settings = _config.StickSettingsFor(physical);
        switch (settings.Mode)
        {
            case StickMode.Disabled:
                return;
            case StickMode.Mouse:
                double speed = settings.MouseSpeed * dt;
                frame.MouseDx += x * speed;
                frame.MouseDy -= y * speed; // screen Y grows downward
                return;
        }

        // Suppress any half-axis whose direction is bound to something else.
        var dirs = ControlInfo.DirectionsOf(physical);
        if (y > 0 && _config.Resolve(dirs[0], _layer) is not null) y = 0;
        if (y < 0 && _config.Resolve(dirs[1], _layer) is not null) y = 0;
        if (x < 0 && _config.Resolve(dirs[2], _layer) is not null) x = 0;
        if (x > 0 && _config.Resolve(dirs[3], _layer) is not null) x = 0;

        var target = _config.SwapSticks ? (physical == Stick.Left ? Stick.Right : Stick.Left) : physical;
        frame.Pad.SetStick(target, x, y);
    }

    private static void Emit(MappingAction action, ref SourceState st, double now, OutputFrame frame, bool rising)
    {
        switch (action)
        {
            case GamepadAction g:
                ApplyGamepadTarget(g.Target, ref frame.Pad);
                break;
            case KeyAction k:
                frame.Keys.Add(k.VirtualKey);
                break;
            case MouseButtonAction m:
                frame.MouseButtons |= OutputFrame.ToFlag(m.Button);
                break;
            case MouseWheelAction w:
                bool fire = rising;
                if (!fire && now - st.ActiveSince >= WheelRepeatDelay && now - st.LastRepeat >= WheelRepeatInterval) fire = true;
                if (fire)
                {
                    st.LastRepeat = now;
                    switch (w.Direction)
                    {
                        case WheelDirection.Up: frame.WheelVertical++; break;
                        case WheelDirection.Down: frame.WheelVertical--; break;
                        case WheelDirection.Right: frame.WheelHorizontal++; break;
                        case WheelDirection.Left: frame.WheelHorizontal--; break;
                    }
                }
                break;
            case ShiftAction:
                break; // handled by layer computation
        }
    }

    internal static void ApplyGamepadTarget(Control target, ref GamepadState pad)
    {
        switch (ControlInfo.KindOf(target))
        {
            case ControlKind.Button:
                pad.Buttons |= ControlInfo.ToButtonFlag(target);
                break;
            case ControlKind.Trigger:
                if (target == Control.LeftTrigger) pad.LeftTrigger = 1f; else pad.RightTrigger = 1f;
                break;
            case ControlKind.StickDirection:
                var (stick, dx, dy) = ControlInfo.StickDirection(target);
                var (x, y) = pad.GetStick(stick);
                if (dx != 0) x = dx;
                if (dy != 0) y = dy;
                pad.SetStick(stick, x, y);
                break;
        }
    }

    private bool IsSourceDown(Control c, in GamepadState input, float lx, float ly, float rx, float ry, bool wasDown)
    {
        switch (ControlInfo.KindOf(c))
        {
            case ControlKind.Trigger:
            {
                var s = _config.TriggerSettingsFor(c);
                float raw = c == Control.LeftTrigger ? input.LeftTrigger : input.RightTrigger;
                float threshold = (float)Math.Clamp(s.DigitalThreshold, 0.02, 1.0);
                return wasDown ? raw >= threshold - Hysteresis : raw >= threshold;
            }
            case ControlKind.StickDirection:
            {
                var (stick, dx, dy) = ControlInfo.StickDirection(c);
                var s = _config.StickSettingsFor(stick);
                float x = stick == Stick.Left ? lx : rx;
                float y = stick == Stick.Left ? ly : ry;
                float along = (x * dx) + (y * dy);
                float threshold = (float)Math.Clamp(s.DirectionThreshold, 0.1, 0.95);
                return wasDown ? along >= threshold - Hysteresis : along >= threshold;
            }
            default:
                return input.IsDown(ControlInfo.ToButtonFlag(c));
        }
    }

    private int ComputeLayer()
    {
        int layer = 0;
        long newest = -1;
        for (int i = 1; i < _state.Length; i++)
        {
            ref var st = ref _state[i];
            if (st.Binding is null) continue;
            bool engaged = st.Binding.Mode is ActivationMode.Toggle or ActivationMode.ToggleTurbo ? st.Toggled : st.Down;
            if (!engaged) continue;
            foreach (var a in st.Binding.Actions)
            {
                if (a is ShiftAction s && st.Sequence > newest)
                {
                    newest = st.Sequence;
                    layer = s.Layer;
                }
            }
        }
        return layer;
    }

    private static bool HasShift(ButtonBinding b)
    {
        foreach (var a in b.Actions) if (a is ShiftAction) return true;
        return false;
    }

    private static bool Pulse(double elapsed, double rateHz)
    {
        double period = 1.0 / Math.Clamp(rateHz, 1, 50);
        return elapsed % period < period / 2;
    }

    private struct SourceState
    {
        public bool Down;
        public bool Toggled;
        public bool WasActive;
        public double PressTime;
        public double ToggleTime;
        public double ActiveSince;
        public double LastRepeat;
        public long Sequence;
        public ButtonBinding? Binding;
    }
}
