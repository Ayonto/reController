namespace ReController.Core.Input;

/// <summary>
/// Every physical or virtual gamepad control reController can read from or write to.
/// Digital buttons, analog triggers (read as digital when past a threshold) and the
/// four cardinal directions of each thumbstick are all addressable as a <see cref="Control"/>.
/// </summary>
/// <remarks>Values are persisted by name in profiles; never rename a member without a migration.</remarks>
public enum Control : byte
{
    None = 0,

    A,
    B,
    X,
    Y,
    LeftBumper,
    RightBumper,
    LeftTrigger,
    RightTrigger,
    Back,
    Start,
    Guide,
    LeftStickClick,
    RightStickClick,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,

    LeftStickUp,
    LeftStickDown,
    LeftStickLeft,
    LeftStickRight,
    RightStickUp,
    RightStickDown,
    RightStickLeft,
    RightStickRight,

    // Extended controls exposed by DualSense / Switch Pro / Elite style pads.
    Misc1,
    Paddle1,
    Paddle2,
    Paddle3,
    Paddle4,
    Touchpad,
}

public enum ControlKind
{
    Button,
    Trigger,
    StickDirection,
}

public enum Stick
{
    Left,
    Right,
}

public static class ControlInfo
{
    /// <summary>All controls that can be a mapping source, in display order.</summary>
    public static IReadOnlyList<Control> All { get; } =
        Enum.GetValues<Control>().Where(c => c != Control.None).ToArray();

    /// <summary>Controls that an Xbox 360 virtual pad can emit.</summary>
    public static IReadOnlyList<Control> VirtualPadTargets { get; } =
        All.Where(c => c < Control.Misc1).ToArray();

    public static ControlKind KindOf(Control c) => c switch
    {
        Control.LeftTrigger or Control.RightTrigger => ControlKind.Trigger,
        >= Control.LeftStickUp and <= Control.RightStickRight => ControlKind.StickDirection,
        _ => ControlKind.Button,
    };

    public static bool IsDigitalButton(Control c) => c != Control.None && KindOf(c) == ControlKind.Button;

    /// <summary>The bit used for a digital button inside <see cref="GamepadButtons"/>.</summary>
    public static GamepadButtons ToButtonFlag(Control c) => c switch
    {
        Control.A => GamepadButtons.A,
        Control.B => GamepadButtons.B,
        Control.X => GamepadButtons.X,
        Control.Y => GamepadButtons.Y,
        Control.LeftBumper => GamepadButtons.LeftBumper,
        Control.RightBumper => GamepadButtons.RightBumper,
        Control.Back => GamepadButtons.Back,
        Control.Start => GamepadButtons.Start,
        Control.Guide => GamepadButtons.Guide,
        Control.LeftStickClick => GamepadButtons.LeftStick,
        Control.RightStickClick => GamepadButtons.RightStick,
        Control.DPadUp => GamepadButtons.DPadUp,
        Control.DPadDown => GamepadButtons.DPadDown,
        Control.DPadLeft => GamepadButtons.DPadLeft,
        Control.DPadRight => GamepadButtons.DPadRight,
        Control.Misc1 => GamepadButtons.Misc1,
        Control.Paddle1 => GamepadButtons.Paddle1,
        Control.Paddle2 => GamepadButtons.Paddle2,
        Control.Paddle3 => GamepadButtons.Paddle3,
        Control.Paddle4 => GamepadButtons.Paddle4,
        Control.Touchpad => GamepadButtons.Touchpad,
        _ => GamepadButtons.None,
    };

    public static (Stick Stick, float X, float Y) StickDirection(Control c) => c switch
    {
        Control.LeftStickUp => (Stick.Left, 0, 1),
        Control.LeftStickDown => (Stick.Left, 0, -1),
        Control.LeftStickLeft => (Stick.Left, -1, 0),
        Control.LeftStickRight => (Stick.Left, 1, 0),
        Control.RightStickUp => (Stick.Right, 0, 1),
        Control.RightStickDown => (Stick.Right, 0, -1),
        Control.RightStickLeft => (Stick.Right, -1, 0),
        Control.RightStickRight => (Stick.Right, 1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "Not a stick direction"),
    };

    public static IReadOnlyList<Control> DirectionsOf(Stick stick) => stick == Stick.Left
        ? [Control.LeftStickUp, Control.LeftStickDown, Control.LeftStickLeft, Control.LeftStickRight]
        : [Control.RightStickUp, Control.RightStickDown, Control.RightStickLeft, Control.RightStickRight];

    public static IReadOnlyList<Control> DPad { get; } =
        [Control.DPadUp, Control.DPadDown, Control.DPadLeft, Control.DPadRight];
}
