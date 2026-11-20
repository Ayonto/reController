using System.Text.Json.Serialization;
using ReController.Core.Input;

namespace ReController.Core.Mapping;

/// <summary>One output produced while a binding is active. A binding may combine several.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GamepadAction), "gamepad")]
[JsonDerivedType(typeof(KeyAction), "key")]
[JsonDerivedType(typeof(MouseButtonAction), "mouseButton")]
[JsonDerivedType(typeof(MouseWheelAction), "mouseWheel")]
[JsonDerivedType(typeof(ShiftAction), "shift")]
public abstract record MappingAction;

/// <summary>Press a control on the virtual gamepad.</summary>
public sealed record GamepadAction(Control Target) : MappingAction;

/// <summary>Hold a keyboard key, identified by Windows virtual-key code.</summary>
public sealed record KeyAction(ushort VirtualKey) : MappingAction;

public enum MouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

public sealed record MouseButtonAction(MouseButton Button) : MappingAction;

public enum WheelDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>Scroll one notch on press, then repeat while held.</summary>
public sealed record MouseWheelAction(WheelDirection Direction) : MappingAction;

/// <summary>Switch to a shift layer (1..<see cref="MappingConfig.MaxShiftLayers"/>) while held, or toggle it.</summary>
public sealed record ShiftAction(int Layer) : MappingAction;
