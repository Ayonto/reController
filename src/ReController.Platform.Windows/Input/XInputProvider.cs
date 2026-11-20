using ReController.Core.Input;
using ReController.Platform.Windows.Native;
using Serilog;
using static ReController.Platform.Windows.Native.XInputNative;

namespace ReController.Platform.Windows.Input;

/// <summary>Reads Xbox (and other XInput) controllers. Virtual pads created by reController are excluded.</summary>
internal sealed class XInputProvider(Func<int, bool> isVirtualSlot) : IInputProvider
{
    private const int StableTicksRequired = 2;

    private readonly bool[] _connected = new bool[MaxControllers];
    private readonly int[] _stableCount = new int[MaxControllers];
    private readonly (float Low, float High)?[] _pendingRumble = new (float, float)?[MaxControllers];
    private readonly object _rumbleGate = new();
    private readonly ILogger _log = Log.ForContext<XInputProvider>();

    public string Name => "XInput";
    public bool IsAvailable { get; private set; }

    public static string KeyFor(int slot) => $"xinput:{slot}";

    public void Initialize()
    {
        try
        {
            _ = GetState(0, out _);
            IsAvailable = true;
        }
        catch (DllNotFoundException ex)
        {
            _log.Error(ex, "xinput1_4.dll not available; XInput controllers will not be detected");
        }
    }

    public void Update()
    {
        lock (_rumbleGate)
        {
            for (int i = 0; i < MaxControllers; i++)
            {
                if (_pendingRumble[i] is not { } r) continue;
                _pendingRumble[i] = null;
                if (!_connected[i]) continue;
                var v = new XInputVibration
                {
                    LeftMotorSpeed = (ushort)(Math.Clamp(r.Low, 0, 1) * ushort.MaxValue),
                    RightMotorSpeed = (ushort)(Math.Clamp(r.High, 0, 1) * ushort.MaxValue),
                };
                _ = XInputSetState(i, ref v);
            }
        }
    }

    public void Enumerate(List<PhysicalDeviceInfo> into)
    {
        if (!IsAvailable) return;
        for (int slot = 0; slot < MaxControllers; slot++)
        {
            bool present = !isVirtualSlot(slot) && GetState(slot, out _) == ErrorSuccess;
            // Debounce: a slot must be seen on consecutive scans before it is reported. This avoids briefly
            // reporting our own freshly plugged virtual pad before ViGEm tells us its user index.
            _stableCount[slot] = present ? Math.Min(_stableCount[slot] + 1, StableTicksRequired) : 0;
            _connected[slot] = _stableCount[slot] >= StableTicksRequired;
            if (!_connected[slot]) continue;

            string name = $"Xbox Controller {slot + 1}";
            if (XInputGetCapabilities(slot, 0, out var caps) == ErrorSuccess)
            {
                name = caps.SubType switch
                {
                    2 => $"Wheel (XInput {slot + 1})",
                    3 => $"Arcade Stick (XInput {slot + 1})",
                    4 => $"Flight Stick (XInput {slot + 1})",
                    _ => $"Xbox Controller {slot + 1}",
                };
            }
            into.Add(new PhysicalDeviceInfo(KeyFor(slot), name, DeviceFamily.Xbox, Name, 0x045E, 0, SupportsExtendedButtons: false));
        }
    }

    public bool TryRead(string key, out GamepadState state)
    {
        state = default;
        if (!key.StartsWith("xinput:", StringComparison.Ordinal) || !int.TryParse(key.AsSpan(7), out int slot) || slot is < 0 or >= MaxControllers)
            return false;
        if (!_connected[slot] || GetState(slot, out var s) != ErrorSuccess) return false;
        state = Convert(s.Gamepad);
        return true;
    }

    public void SetRumble(string key, float low, float high)
    {
        if (!key.StartsWith("xinput:", StringComparison.Ordinal) || !int.TryParse(key.AsSpan(7), out int slot) || slot is < 0 or >= MaxControllers) return;
        lock (_rumbleGate) _pendingRumble[slot] = (low, high);
    }

    internal static GamepadState Convert(in XInputGamepad g)
    {
        var b = GamepadButtons.None;
        ushort w = g.Buttons;
        if ((w & A) != 0) b |= GamepadButtons.A;
        if ((w & B) != 0) b |= GamepadButtons.B;
        if ((w & X) != 0) b |= GamepadButtons.X;
        if ((w & Y) != 0) b |= GamepadButtons.Y;
        if ((w & LeftShoulder) != 0) b |= GamepadButtons.LeftBumper;
        if ((w & RightShoulder) != 0) b |= GamepadButtons.RightBumper;
        if ((w & Back) != 0) b |= GamepadButtons.Back;
        if ((w & Start) != 0) b |= GamepadButtons.Start;
        if ((w & Guide) != 0) b |= GamepadButtons.Guide;
        if ((w & LeftThumb) != 0) b |= GamepadButtons.LeftStick;
        if ((w & RightThumb) != 0) b |= GamepadButtons.RightStick;
        if ((w & DPadUp) != 0) b |= GamepadButtons.DPadUp;
        if ((w & DPadDown) != 0) b |= GamepadButtons.DPadDown;
        if ((w & DPadLeft) != 0) b |= GamepadButtons.DPadLeft;
        if ((w & DPadRight) != 0) b |= GamepadButtons.DPadRight;
        return new GamepadState
        {
            Buttons = b,
            LeftX = Axis(g.ThumbLX),
            LeftY = Axis(g.ThumbLY),
            RightX = Axis(g.ThumbRX),
            RightY = Axis(g.ThumbRY),
            LeftTrigger = g.LeftTrigger / 255f,
            RightTrigger = g.RightTrigger / 255f,
        };
    }

    private static float Axis(short v) => Math.Clamp(v / 32767f, -1f, 1f);

    public void Dispose()
    {
        for (int i = 0; i < MaxControllers; i++)
        {
            if (!_connected[i]) continue;
            var v = default(XInputVibration);
            _ = XInputSetState(i, ref v);
        }
    }
}
