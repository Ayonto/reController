using ReController.Core.Input;
using ReController.Core.Mapping;

namespace ReController.Core.Tests;

public class MappingEngineTests
{
    private static MappingConfig Config(Action<MappingConfig>? setup = null)
    {
        var c = MappingConfig.CreateDefault();
        setup?.Invoke(c);
        c.Normalize();
        return c;
    }

    private static ButtonBinding Bind(Control source, params MappingAction[] actions) =>
        new() { Source = source, Actions = [.. actions] };

    private static OutputFrame Run(MappingEngine e, GamepadState s, double t)
    {
        var f = new OutputFrame();
        e.Process(s, t, f);
        return f;
    }

    private static GamepadState Press(GamepadButtons b) => new() { Buttons = b };

    [Fact]
    public void Unbound_buttons_pass_through()
    {
        var e = new MappingEngine(Config());
        var f = Run(e, Press(GamepadButtons.A | GamepadButtons.DPadLeft), 0);
        Assert.Equal(GamepadButtons.A | GamepadButtons.DPadLeft, f.Pad.Buttons);
    }

    [Fact]
    public void Remapped_button_emits_target_and_not_source()
    {
        var e = new MappingEngine(Config(c => c.Layers[0].Set(Bind(Control.LeftBumper, new GamepadAction(Control.A)))));
        var f = Run(e, Press(GamepadButtons.LeftBumper), 0);
        Assert.Equal(GamepadButtons.A, f.Pad.Buttons);
    }

    [Fact]
    public void Disabled_binding_suppresses_output()
    {
        var e = new MappingEngine(Config(c => c.Layers[0].Set(Bind(Control.B))));
        var f = Run(e, Press(GamepadButtons.B), 0);
        Assert.Equal(GamepadButtons.None, f.Pad.Buttons);
    }

    [Fact]
    public void Keyboard_combo_holds_all_keys_then_releases()
    {
        var e = new MappingEngine(Config(c => c.Layers[0].Set(Bind(Control.X, new KeyAction(0x11), new KeyAction(0x43)))));
        var down = Run(e, Press(GamepadButtons.X), 0);
        Assert.True(down.Keys.SetEquals(new ushort[] { 0x11, 0x43 }));
        var up = Run(e, default, 0.01);
        Assert.Empty(up.Keys);
    }

    [Fact]
    public void Toggle_latches_until_next_press()
    {
        var e = new MappingEngine(Config(c =>
        {
            var b = Bind(Control.Y, new KeyAction(0x20));
            b.Mode = ActivationMode.Toggle;
            c.Layers[0].Set(b);
        }));
        Assert.Contains((ushort)0x20, Run(e, Press(GamepadButtons.Y), 0.00).Keys);
        Assert.Contains((ushort)0x20, Run(e, default, 0.01).Keys);
        Assert.Empty(Run(e, Press(GamepadButtons.Y), 0.02).Keys);
        Assert.Empty(Run(e, default, 0.03).Keys);
    }

    [Fact]
    public void Turbo_pulses_while_held()
    {
        var e = new MappingEngine(Config(c =>
        {
            var b = Bind(Control.A, new GamepadAction(Control.A));
            b.Mode = ActivationMode.Turbo;
            b.TurboRateHz = 10; // 100 ms period: on for 50 ms, off for 50 ms
            c.Layers[0].Set(b);
        }));
        Assert.True(Run(e, Press(GamepadButtons.A), 0.000).Pad.IsDown(GamepadButtons.A));
        Assert.True(Run(e, Press(GamepadButtons.A), 0.030).Pad.IsDown(GamepadButtons.A));
        Assert.False(Run(e, Press(GamepadButtons.A), 0.070).Pad.IsDown(GamepadButtons.A));
        Assert.True(Run(e, Press(GamepadButtons.A), 0.110).Pad.IsDown(GamepadButtons.A));
        Assert.False(Run(e, default, 0.120).Pad.IsDown(GamepadButtons.A));
    }

    [Fact]
    public void Shift_layer_overrides_and_inherits_main()
    {
        var e = new MappingEngine(Config(c =>
        {
            c.Layers[0].Set(Bind(Control.Back, new ShiftAction(1)));
            c.Layers[1].Set(Bind(Control.A, new KeyAction(0x46)));
        }));
        // Shift held + A pressed on the same tick: A uses the shifted binding.
        var f = Run(e, Press(GamepadButtons.Back | GamepadButtons.A), 0);
        Assert.Equal(1, f.ActiveLayer);
        Assert.Contains((ushort)0x46, f.Keys);
        Assert.False(f.Pad.IsDown(GamepadButtons.A));
        Assert.False(f.Pad.IsDown(GamepadButtons.Back));
        // Unbound in shift layer -> inherits main (passthrough).
        f = Run(e, Press(GamepadButtons.Back | GamepadButtons.B), 0.01);
        Assert.True(f.Pad.IsDown(GamepadButtons.B));
    }

    [Fact]
    public void Binding_captured_at_press_is_released_after_layer_change()
    {
        var e = new MappingEngine(Config(c =>
        {
            c.Layers[0].Set(Bind(Control.Back, new ShiftAction(1)));
            c.Layers[1].Set(Bind(Control.A, new KeyAction(0x46)));
        }));
        Run(e, Press(GamepadButtons.Back), 0);
        Assert.Contains((ushort)0x46, Run(e, Press(GamepadButtons.Back | GamepadButtons.A), 0.01).Keys);
        // Release shift while still holding A: A keeps the shifted binding (no switch to passthrough mid-press).
        var f = Run(e, Press(GamepadButtons.A), 0.02);
        Assert.Equal(0, f.ActiveLayer);
        Assert.Contains((ushort)0x46, f.Keys);
        Assert.False(f.Pad.IsDown(GamepadButtons.A));
        // Release A: key released, nothing stuck.
        f = Run(e, default, 0.03);
        Assert.Empty(f.Keys);
        // Next press of A uses main layer again.
        Assert.True(Run(e, Press(GamepadButtons.A), 0.04).Pad.IsDown(GamepadButtons.A));
    }

    [Fact]
    public void Trigger_passes_through_analog_and_can_be_remapped_digitally()
    {
        var plain = new MappingEngine(Config(c => c.LeftTrigger.Deadzone = 0));
        Assert.Equal(0.5f, Run(plain, new GamepadState { LeftTrigger = 0.5f }, 0).Pad.LeftTrigger, 3);

        var mapped = new MappingEngine(Config(c => c.Layers[0].Set(Bind(Control.LeftTrigger, new MouseButtonAction(MouseButton.Right)))));
        var f = Run(mapped, new GamepadState { LeftTrigger = 0.8f }, 0);
        Assert.Equal(0f, f.Pad.LeftTrigger);
        Assert.Equal(MouseButtons.Right, f.MouseButtons);
    }

    [Fact]
    public void Stick_direction_binding_suppresses_only_that_half_axis()
    {
        var e = new MappingEngine(Config(c =>
        {
            c.LeftStick.Deadzone = 0;
            c.Layers[0].Set(Bind(Control.LeftStickUp, new KeyAction(0x57)));
        }));
        var f = Run(e, new GamepadState { LeftX = 0.6f, LeftY = 0.8f }, 0);
        Assert.Contains((ushort)0x57, f.Keys);
        Assert.Equal(0f, f.Pad.LeftY);
        Assert.True(f.Pad.LeftX > 0.5f);

        f = Run(e, new GamepadState { LeftY = -0.9f }, 0.01);
        Assert.Empty(f.Keys);
        Assert.True(f.Pad.LeftY < -0.8f);
    }

    [Fact]
    public void Button_can_drive_stick_direction()
    {
        var e = new MappingEngine(Config(c => c.Layers[0].Set(Bind(Control.Paddle1, new GamepadAction(Control.RightStickLeft)))));
        var f = Run(e, Press(GamepadButtons.Paddle1), 0);
        Assert.Equal(-1f, f.Pad.RightX);
    }

    [Fact]
    public void Swap_sticks_routes_physical_left_to_virtual_right()
    {
        var e = new MappingEngine(Config(c => { c.SwapSticks = true; c.LeftStick.Deadzone = 0; }));
        var f = Run(e, new GamepadState { LeftX = 1f }, 0);
        Assert.Equal(1f, f.Pad.RightX);
        Assert.Equal(0f, f.Pad.LeftX);
    }

    [Fact]
    public void Stick_mouse_mode_moves_cursor_proportionally_to_time()
    {
        var e = new MappingEngine(Config(c => { c.RightStick.Mode = StickMode.Mouse; c.RightStick.Deadzone = 0; c.RightStick.MouseSpeed = 1000; }));
        Run(e, new GamepadState { RightX = 1f }, 0);
        var f = Run(e, new GamepadState { RightX = 1f, RightY = 0 }, 0.010);
        Assert.Equal(10, f.MouseDx, 3);
        Assert.Equal(0f, f.Pad.RightX);
    }

    [Fact]
    public void Wheel_fires_once_then_repeats_after_delay()
    {
        var e = new MappingEngine(Config(c => c.Layers[0].Set(Bind(Control.DPadDown, new MouseWheelAction(WheelDirection.Down)))));
        var press = Press(GamepadButtons.DPadDown);
        Assert.Equal(-1, Run(e, press, 0.00).WheelVertical);
        Assert.Equal(0, Run(e, press, 0.10).WheelVertical);
        Assert.Equal(-1, Run(e, press, 0.40).WheelVertical);
    }

    [Fact]
    public void Reset_releases_everything()
    {
        var e = new MappingEngine(Config(c => c.Layers[0].Set(Bind(Control.A, new KeyAction(0x41)))));
        Run(e, Press(GamepadButtons.A), 0);
        e.Reset();
        Assert.Empty(Run(e, default, 0.01).Keys);
    }
}
