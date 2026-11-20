using System.Runtime.InteropServices;

namespace ReController.Core.Input;

[Flags]
public enum GamepadButtons : uint
{
    None = 0,
    A = 1u << 0,
    B = 1u << 1,
    X = 1u << 2,
    Y = 1u << 3,
    LeftBumper = 1u << 4,
    RightBumper = 1u << 5,
    Back = 1u << 6,
    Start = 1u << 7,
    Guide = 1u << 8,
    LeftStick = 1u << 9,
    RightStick = 1u << 10,
    DPadUp = 1u << 11,
    DPadDown = 1u << 12,
    DPadLeft = 1u << 13,
    DPadRight = 1u << 14,
    Misc1 = 1u << 15,
    Paddle1 = 1u << 16,
    Paddle2 = 1u << 17,
    Paddle3 = 1u << 18,
    Paddle4 = 1u << 19,
    Touchpad = 1u << 20,
}

/// <summary>
/// Normalised, device-independent snapshot of a gamepad.
/// Sticks are in [-1, 1] with +Y pointing <b>up</b> (XInput convention); triggers are in [0, 1].
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GamepadState : IEquatable<GamepadState>
{
    public GamepadButtons Buttons;
    public float LeftX;
    public float LeftY;
    public float RightX;
    public float RightY;
    public float LeftTrigger;
    public float RightTrigger;

    public static GamepadState Neutral => default;

    public readonly bool IsDown(GamepadButtons b) => (Buttons & b) != 0;

    public readonly (float X, float Y) GetStick(Stick s) => s == Stick.Left ? (LeftX, LeftY) : (RightX, RightY);

    public void SetStick(Stick s, float x, float y)
    {
        if (s == Stick.Left) { LeftX = x; LeftY = y; }
        else { RightX = x; RightY = y; }
    }

    public readonly bool Equals(GamepadState other) =>
        Buttons == other.Buttons &&
        LeftX.Equals(other.LeftX) && LeftY.Equals(other.LeftY) &&
        RightX.Equals(other.RightX) && RightY.Equals(other.RightY) &&
        LeftTrigger.Equals(other.LeftTrigger) && RightTrigger.Equals(other.RightTrigger);

    public override readonly bool Equals(object? obj) => obj is GamepadState s && Equals(s);

    public override readonly int GetHashCode() =>
        HashCode.Combine(Buttons, LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger);

    public static bool operator ==(GamepadState a, GamepadState b) => a.Equals(b);
    public static bool operator !=(GamepadState a, GamepadState b) => !a.Equals(b);
}
