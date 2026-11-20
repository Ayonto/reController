using System.Runtime.InteropServices;

namespace ReController.Platform.Windows.Native;

/// <summary>Minimal SDL3 surface used for non-XInput controllers (DualShock, DualSense, Switch Pro, generic HID).</summary>
internal static unsafe partial class SdlNative
{
    private const string Lib = "SDL3";

    public const uint InitGamepad = 0x00002000;

    public const int ButtonSouth = 0, ButtonEast = 1, ButtonWest = 2, ButtonNorth = 3, ButtonBack = 4, ButtonGuide = 5,
        ButtonStart = 6, ButtonLeftStick = 7, ButtonRightStick = 8, ButtonLeftShoulder = 9, ButtonRightShoulder = 10,
        ButtonDPadUp = 11, ButtonDPadDown = 12, ButtonDPadLeft = 13, ButtonDPadRight = 14, ButtonMisc1 = 15,
        ButtonRightPaddle1 = 16, ButtonLeftPaddle1 = 17, ButtonRightPaddle2 = 18, ButtonLeftPaddle2 = 19, ButtonTouchpad = 20;

    public const int AxisLeftX = 0, AxisLeftY = 1, AxisRightX = 2, AxisRightY = 3, AxisLeftTrigger = 4, AxisRightTrigger = 5;

    public enum GamepadType
    {
        Unknown = 0,
        Standard,
        Xbox360,
        XboxOne,
        PS3,
        PS4,
        PS5,
        SwitchPro,
        JoyConLeft,
        JoyConRight,
        JoyConPair,
    }

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_Init(uint flags);

    [LibraryImport(Lib)]
    public static partial void SDL_Quit();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetHint(string name, string value);

    [LibraryImport(Lib)]
    public static partial byte* SDL_GetError();

    [LibraryImport(Lib)]
    public static partial void SDL_free(void* mem);

    [LibraryImport(Lib)]
    public static partial void SDL_PumpEvents();

    [LibraryImport(Lib)]
    public static partial void SDL_FlushEvents(uint minType, uint maxType);

    [LibraryImport(Lib)]
    public static partial void SDL_SetGamepadEventsEnabled([MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(Lib)]
    public static partial void SDL_SetJoystickEventsEnabled([MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(Lib)]
    public static partial uint* SDL_GetGamepads(out int count);

    [LibraryImport(Lib)]
    public static partial IntPtr SDL_OpenGamepad(uint instanceId);

    [LibraryImport(Lib)]
    public static partial void SDL_CloseGamepad(IntPtr gamepad);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GamepadConnected(IntPtr gamepad);

    [LibraryImport(Lib)]
    public static partial byte* SDL_GetGamepadNameForID(uint instanceId);

    [LibraryImport(Lib)]
    public static partial byte* SDL_GetGamepadPathForID(uint instanceId);

    [LibraryImport(Lib)]
    public static partial GamepadType SDL_GetGamepadTypeForID(uint instanceId);

    [LibraryImport(Lib)]
    public static partial ushort SDL_GetGamepadVendorForID(uint instanceId);

    [LibraryImport(Lib)]
    public static partial ushort SDL_GetGamepadProductForID(uint instanceId);

    [LibraryImport(Lib)]
    public static partial byte* SDL_GetGamepadSerial(IntPtr gamepad);

    [LibraryImport(Lib)]
    public static partial void SDL_UpdateGamepads();

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GetGamepadButton(IntPtr gamepad, int button);

    [LibraryImport(Lib)]
    public static partial short SDL_GetGamepadAxis(IntPtr gamepad, int axis);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_RumbleGamepad(IntPtr gamepad, ushort lowFrequency, ushort highFrequency, uint durationMs);

    public static string? Utf8(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((IntPtr)p);

    public static string LastError() => Utf8(SDL_GetError()) ?? "unknown SDL error";
}
