using ReController.Core.Input;

namespace ReController.Core.Mapping;

[Flags]
public enum MouseButtons : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    X1 = 8,
    X2 = 16,
}

/// <summary>
/// The <i>desired</i> output state for one engine tick. Sinks diff successive frames
/// (e.g. key-down only when a key newly appears), which makes stuck keys impossible:
/// an empty frame always releases everything.
/// </summary>
public sealed class OutputFrame
{
    public GamepadState Pad;
    public HashSet<ushort> Keys { get; } = [];
    public MouseButtons MouseButtons;

    /// <summary>Relative cursor movement for this tick, in (fractional) pixels. +Y = down.</summary>
    public double MouseDx;
    public double MouseDy;

    /// <summary>Wheel notches for this tick. +Vertical = away from user, +Horizontal = right.</summary>
    public int WheelVertical;
    public int WheelHorizontal;

    public int ActiveLayer;

    public void Clear()
    {
        Pad = default;
        Keys.Clear();
        MouseButtons = MouseButtons.None;
        MouseDx = MouseDy = 0;
        WheelVertical = WheelHorizontal = 0;
        ActiveLayer = 0;
    }

    public static MouseButtons ToFlag(MouseButton b) => b switch
    {
        MouseButton.Left => MouseButtons.Left,
        MouseButton.Right => MouseButtons.Right,
        MouseButton.Middle => MouseButtons.Middle,
        MouseButton.X1 => MouseButtons.X1,
        MouseButton.X2 => MouseButtons.X2,
        _ => MouseButtons.None,
    };
}
