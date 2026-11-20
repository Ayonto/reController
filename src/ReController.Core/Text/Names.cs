using ReController.Core.Input;
using ReController.Core.Mapping;

namespace ReController.Core.Text;

/// <summary>Human-readable names for controls, keys and actions.</summary>
public static class Names
{
    public static string Short(Control c) => c switch
    {
        Control.A => "A",
        Control.B => "B",
        Control.X => "X",
        Control.Y => "Y",
        Control.LeftBumper => "LB",
        Control.RightBumper => "RB",
        Control.LeftTrigger => "LT",
        Control.RightTrigger => "RT",
        Control.Back => "View",
        Control.Start => "Menu",
        Control.Guide => "Guide",
        Control.LeftStickClick => "LS",
        Control.RightStickClick => "RS",
        Control.DPadUp => "D↑",
        Control.DPadDown => "D↓",
        Control.DPadLeft => "D←",
        Control.DPadRight => "D→",
        Control.LeftStickUp => "LS↑",
        Control.LeftStickDown => "LS↓",
        Control.LeftStickLeft => "LS←",
        Control.LeftStickRight => "LS→",
        Control.RightStickUp => "RS↑",
        Control.RightStickDown => "RS↓",
        Control.RightStickLeft => "RS←",
        Control.RightStickRight => "RS→",
        Control.Misc1 => "Share",
        Control.Paddle1 => "P1",
        Control.Paddle2 => "P2",
        Control.Paddle3 => "P3",
        Control.Paddle4 => "P4",
        Control.Touchpad => "Touchpad",
        _ => "—",
    };

    public static string Long(Control c) => c switch
    {
        Control.A => "A button",
        Control.B => "B button",
        Control.X => "X button",
        Control.Y => "Y button",
        Control.LeftBumper => "Left bumper",
        Control.RightBumper => "Right bumper",
        Control.LeftTrigger => "Left trigger",
        Control.RightTrigger => "Right trigger",
        Control.Back => "View / Back",
        Control.Start => "Menu / Start",
        Control.Guide => "Guide",
        Control.LeftStickClick => "Left stick click",
        Control.RightStickClick => "Right stick click",
        Control.DPadUp => "D-Pad up",
        Control.DPadDown => "D-Pad down",
        Control.DPadLeft => "D-Pad left",
        Control.DPadRight => "D-Pad right",
        Control.LeftStickUp => "Left stick up",
        Control.LeftStickDown => "Left stick down",
        Control.LeftStickLeft => "Left stick left",
        Control.LeftStickRight => "Left stick right",
        Control.RightStickUp => "Right stick up",
        Control.RightStickDown => "Right stick down",
        Control.RightStickLeft => "Right stick left",
        Control.RightStickRight => "Right stick right",
        Control.Misc1 => "Share / Capture",
        Control.Paddle1 => "Paddle 1 (upper right)",
        Control.Paddle2 => "Paddle 2 (upper left)",
        Control.Paddle3 => "Paddle 3 (lower right)",
        Control.Paddle4 => "Paddle 4 (lower left)",
        Control.Touchpad => "Touchpad click",
        _ => "None",
    };

    public static string Mouse(MouseButton b) => b switch
    {
        MouseButton.Left => "LMB",
        MouseButton.Right => "RMB",
        MouseButton.Middle => "MMB",
        MouseButton.X1 => "Mouse 4",
        MouseButton.X2 => "Mouse 5",
        _ => b.ToString(),
    };

    public static string Wheel(WheelDirection d) => d switch
    {
        WheelDirection.Up => "Wheel ↑",
        WheelDirection.Down => "Wheel ↓",
        WheelDirection.Left => "Wheel ←",
        WheelDirection.Right => "Wheel →",
        _ => d.ToString(),
    };

    public static string Action(MappingAction a) => a switch
    {
        GamepadAction g => Short(g.Target),
        KeyAction k => Key(k.VirtualKey),
        MouseButtonAction m => Mouse(m.Button),
        MouseWheelAction w => Wheel(w.Direction),
        ShiftAction s => $"Shift {s.Layer}",
        _ => "?",
    };

    /// <summary>Compact label for a binding as drawn next to the controller.</summary>
    public static string Binding(ButtonBinding b)
    {
        if (b.IsDisabled) return "Disabled";
        string text = string.Join(" + ", b.Actions.Select(Action));
        return b.Mode switch
        {
            ActivationMode.Toggle => text + " ⇄",
            ActivationMode.Turbo => text + " ⚡",
            ActivationMode.ToggleTurbo => text + " ⇄⚡",
            _ => text,
        };
    }

    public static string Key(ushort vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x10 => "Shift",
        0x11 => "Ctrl",
        0x12 => "Alt",
        0x13 => "Pause",
        0x14 => "Caps Lock",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x2C => "Print Screen",
        0x2D => "Insert",
        0x2E => "Delete",
        0x5B => "Win",
        0x5C => "Right Win",
        0x5D => "Menu",
        0x6A => "Num *",
        0x6B => "Num +",
        0x6D => "Num -",
        0x6E => "Num .",
        0x6F => "Num /",
        0x90 => "Num Lock",
        0x91 => "Scroll Lock",
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        0xA2 => "Left Ctrl",
        0xA3 => "Right Ctrl",
        0xA4 => "Left Alt",
        0xA5 => "Right Alt",
        0xAD => "Mute",
        0xAE => "Volume -",
        0xAF => "Volume +",
        0xB0 => "Next track",
        0xB1 => "Prev track",
        0xB3 => "Play/Pause",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => $"VK 0x{vk:X2}",
    };
}
