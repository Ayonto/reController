using System.Diagnostics;
using ReController.Core.Input;
using Serilog;
using static ReController.Platform.Windows.Native.SdlNative;

namespace ReController.Platform.Windows.Input;

/// <summary>
/// Reads PlayStation, Nintendo and generic controllers through SDL3. XInput devices are deliberately
/// left to <see cref="XInputProvider"/> so that our own virtual Xbox pads are never read back.
/// All SDL calls happen on the engine thread.
/// </summary>
internal sealed unsafe class SdlInputProvider : IInputProvider
{
    private readonly Dictionary<uint, OpenPad> _byInstance = [];
    private readonly Dictionary<string, OpenPad> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float Low, float High)> _pendingRumble = new(StringComparer.Ordinal);
    private readonly object _rumbleGate = new();
    private readonly ILogger _log = Log.ForContext<SdlInputProvider>();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastPumpMs;

    public string Name => "SDL";
    public bool IsAvailable { get; private set; }

    public void Initialize()
    {
        try
        {
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            SDL_SetHint("SDL_XINPUT_ENABLED", "0");
            SDL_SetHint("SDL_JOYSTICK_WGI", "0");
            SDL_SetHint("SDL_JOYSTICK_RAWINPUT", "0");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_XBOX", "0");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_XBOX_360", "0");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_XBOX_ONE", "0");
            SDL_SetHint("SDL_JOYSTICK_THREAD", "1");
            if (!SDL_Init(InitGamepad))
            {
                _log.Error("SDL_Init failed: {Error}", LastError());
                return;
            }
            SDL_SetGamepadEventsEnabled(false);
            SDL_SetJoystickEventsEnabled(false);
            IsAvailable = true;
            _log.Information("SDL3 gamepad backend initialised");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _log.Error(ex, "SDL3 could not be loaded; PlayStation / Nintendo controllers will not be detected");
        }
    }

    public void Update()
    {
        if (!IsAvailable) return;
        long ms = _clock.ElapsedMilliseconds;
        if (ms - _lastPumpMs >= 16)
        {
            _lastPumpMs = ms;
            SDL_PumpEvents();
            SDL_FlushEvents(0, uint.MaxValue);
        }
        SDL_UpdateGamepads();

        lock (_rumbleGate)
        {
            foreach (var (key, r) in _pendingRumble)
            {
                if (_byKey.TryGetValue(key, out var pad))
                {
                    // Re-armed by the virtual pad's feedback; a long duration keeps sustained rumble alive.
                    SDL_RumbleGamepad(pad.Handle, (ushort)(r.Low * ushort.MaxValue), (ushort)(r.High * ushort.MaxValue), r.Low + r.High > 0 ? 5000u : 0u);
                }
            }
            _pendingRumble.Clear();
        }
    }

    public void Enumerate(List<PhysicalDeviceInfo> into)
    {
        if (!IsAvailable) return;
        var seen = new HashSet<uint>();
        uint* ids = SDL_GetGamepads(out int count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                uint id = ids[i];
                var type = SDL_GetGamepadTypeForID(id);
                ushort vid = SDL_GetGamepadVendorForID(id);
                if (type is GamepadType.Xbox360 or GamepadType.XboxOne || vid == 0x045E) continue; // handled by XInput
                seen.Add(id);

                if (!_byInstance.TryGetValue(id, out var pad))
                {
                    IntPtr handle = SDL_OpenGamepad(id);
                    if (handle == IntPtr.Zero)
                    {
                        _log.Warning("Could not open SDL gamepad {Id}: {Error}", id, LastError());
                        continue;
                    }
                    ushort pid = SDL_GetGamepadProductForID(id);
                    string name = Utf8(SDL_GetGamepadNameForID(id)) ?? "Controller";
                    string identity = Utf8(SDL_GetGamepadSerial(handle)) is { Length: > 0 } serial
                        ? serial
                        : Utf8(SDL_GetGamepadPathForID(id)) ?? id.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
                    string key = $"sdl:{vid:x4}:{pid:x4}:{StableHash(identity)}";
                    // Two identical pads without serials: disambiguate.
                    while (_byKey.ContainsKey(key)) key += "+";
                    var family = type switch
                    {
                        GamepadType.PS3 or GamepadType.PS4 or GamepadType.PS5 => DeviceFamily.PlayStation,
                        GamepadType.SwitchPro or GamepadType.JoyConLeft or GamepadType.JoyConRight or GamepadType.JoyConPair => DeviceFamily.Nintendo,
                        _ => DeviceFamily.Generic,
                    };
                    pad = new OpenPad(id, handle, new PhysicalDeviceInfo(key, name, family, Name, vid, pid, SupportsExtendedButtons: true));
                    _byInstance[id] = pad;
                    _byKey[key] = pad;
                    _log.Information("Controller connected: {Name} ({Key})", name, key);
                }
                into.Add(pad.Info);
            }
        }
        finally
        {
            if (ids != null) SDL_free(ids);
        }

        foreach (var gone in _byInstance.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            var pad = _byInstance[gone];
            SDL_CloseGamepad(pad.Handle);
            _byInstance.Remove(gone);
            _byKey.Remove(pad.Info.Key);
            _log.Information("Controller disconnected: {Name}", pad.Info.Name);
        }
    }

    public bool TryRead(string key, out GamepadState state)
    {
        state = default;
        if (!_byKey.TryGetValue(key, out var pad)) return false;
        IntPtr h = pad.Handle;
        if (!SDL_GamepadConnected(h)) return false;

        var b = GamepadButtons.None;
        if (SDL_GetGamepadButton(h, ButtonSouth)) b |= GamepadButtons.A;
        if (SDL_GetGamepadButton(h, ButtonEast)) b |= GamepadButtons.B;
        if (SDL_GetGamepadButton(h, ButtonWest)) b |= GamepadButtons.X;
        if (SDL_GetGamepadButton(h, ButtonNorth)) b |= GamepadButtons.Y;
        if (SDL_GetGamepadButton(h, ButtonLeftShoulder)) b |= GamepadButtons.LeftBumper;
        if (SDL_GetGamepadButton(h, ButtonRightShoulder)) b |= GamepadButtons.RightBumper;
        if (SDL_GetGamepadButton(h, ButtonBack)) b |= GamepadButtons.Back;
        if (SDL_GetGamepadButton(h, ButtonStart)) b |= GamepadButtons.Start;
        if (SDL_GetGamepadButton(h, ButtonGuide)) b |= GamepadButtons.Guide;
        if (SDL_GetGamepadButton(h, ButtonLeftStick)) b |= GamepadButtons.LeftStick;
        if (SDL_GetGamepadButton(h, ButtonRightStick)) b |= GamepadButtons.RightStick;
        if (SDL_GetGamepadButton(h, ButtonDPadUp)) b |= GamepadButtons.DPadUp;
        if (SDL_GetGamepadButton(h, ButtonDPadDown)) b |= GamepadButtons.DPadDown;
        if (SDL_GetGamepadButton(h, ButtonDPadLeft)) b |= GamepadButtons.DPadLeft;
        if (SDL_GetGamepadButton(h, ButtonDPadRight)) b |= GamepadButtons.DPadRight;
        if (SDL_GetGamepadButton(h, ButtonMisc1)) b |= GamepadButtons.Misc1;
        if (SDL_GetGamepadButton(h, ButtonRightPaddle1)) b |= GamepadButtons.Paddle1;
        if (SDL_GetGamepadButton(h, ButtonLeftPaddle1)) b |= GamepadButtons.Paddle2;
        if (SDL_GetGamepadButton(h, ButtonRightPaddle2)) b |= GamepadButtons.Paddle3;
        if (SDL_GetGamepadButton(h, ButtonLeftPaddle2)) b |= GamepadButtons.Paddle4;
        if (SDL_GetGamepadButton(h, ButtonTouchpad)) b |= GamepadButtons.Touchpad;

        state = new GamepadState
        {
            Buttons = b,
            LeftX = Axis(SDL_GetGamepadAxis(h, AxisLeftX)),
            LeftY = -Axis(SDL_GetGamepadAxis(h, AxisLeftY)), // SDL: +Y is down
            RightX = Axis(SDL_GetGamepadAxis(h, AxisRightX)),
            RightY = -Axis(SDL_GetGamepadAxis(h, AxisRightY)),
            LeftTrigger = Math.Clamp(SDL_GetGamepadAxis(h, AxisLeftTrigger) / 32767f, 0f, 1f),
            RightTrigger = Math.Clamp(SDL_GetGamepadAxis(h, AxisRightTrigger) / 32767f, 0f, 1f),
        };
        return true;
    }

    public void SetRumble(string key, float low, float high)
    {
        lock (_rumbleGate) _pendingRumble[key] = (Math.Clamp(low, 0, 1), Math.Clamp(high, 0, 1));
    }

    private static float Axis(short v) => Math.Clamp(v / 32767f, -1f, 1f);

    private static string StableHash(string s)
    {
        // FNV-1a: stable across runs (string.GetHashCode is randomised per process).
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h.ToString("x8", global::System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (!IsAvailable) return;
        foreach (var pad in _byInstance.Values) SDL_CloseGamepad(pad.Handle);
        _byInstance.Clear();
        _byKey.Clear();
        SDL_Quit();
        IsAvailable = false;
    }

    private sealed record OpenPad(uint InstanceId, IntPtr Handle, PhysicalDeviceInfo Info);
}
