using System.Runtime.InteropServices;
using ReController.Core.Mapping;
using Serilog;
using static ReController.Platform.Windows.Native.Win32;
using NativeInput = ReController.Platform.Windows.Native.Win32.Input;

namespace ReController.Platform.Windows.Output;

/// <summary>
/// Turns desired keyboard / mouse state into SendInput events by diffing against what is currently held.
/// Keys are injected as hardware scan codes so DirectInput / Raw Input games receive them.
/// </summary>
internal sealed class KeyboardMouseSink : IDisposable
{
    private static readonly int InputSize = Marshal.SizeOf<NativeInput>();
    private static readonly IntPtr Signature = new(0x52434E54); // "RCNT": marks our injected events

    private readonly HashSet<ushort> _held = [];
    private readonly List<ushort> _scratch = [];
    private readonly List<NativeInput> _batch = new(32);
    private MouseButtons _mouseHeld;
    private double _remX, _remY;
    private int _failures;

    public void Apply(OutputFrame desired)
    {
        _batch.Clear();

        // Releases first so a key swapped for another never overlaps.
        _scratch.Clear();
        foreach (var k in _held) if (!desired.Keys.Contains(k)) _scratch.Add(k);
        foreach (var k in _scratch) { _held.Remove(k); AddKey(k, up: true); }
        foreach (var k in desired.Keys) if (_held.Add(k)) AddKey(k, up: false);

        var mouseUp = _mouseHeld & ~desired.MouseButtons;
        var mouseDown = desired.MouseButtons & ~_mouseHeld;
        AddMouseButtons(mouseUp, up: true);
        AddMouseButtons(mouseDown, up: false);
        _mouseHeld = desired.MouseButtons;

        _remX += desired.MouseDx;
        _remY += desired.MouseDy;
        int dx = (int)Math.Truncate(_remX);
        int dy = (int)Math.Truncate(_remY);
        if (dx != 0 || dy != 0)
        {
            _remX -= dx;
            _remY -= dy;
            AddMouse(MouseEventMove, 0, dx, dy);
        }
        if (desired.WheelVertical != 0) AddMouse(MouseEventWheel, unchecked((uint)(desired.WheelVertical * WheelDelta)));
        if (desired.WheelHorizontal != 0) AddMouse(MouseEventHWheel, unchecked((uint)(desired.WheelHorizontal * WheelDelta)));

        Flush();
    }

    public void ReleaseAll()
    {
        var empty = new OutputFrame();
        Apply(empty);
        _remX = _remY = 0;
    }

    private void AddKey(ushort vk, bool up)
    {
        uint sc = MapVirtualKey(vk, MapVkToVscEx);
        var ki = new KeyboardInput { ExtraInfo = Signature };
        if (sc != 0 && !IsVkOnly(vk))
        {
            ki.ScanCode = (ushort)(sc & 0xFF);
            ki.Flags = KeyEventScanCode | ((sc & 0xFF00) is 0xE000 or 0xE100 ? KeyEventExtendedKey : 0);
        }
        else
        {
            ki.VirtualKey = vk;
        }
        if (up) ki.Flags |= KeyEventKeyUp;
        _batch.Add(new NativeInput { Type = InputKeyboard, Data = new InputUnion { Keyboard = ki } });
    }

    /// <summary>Media / browser keys have no useful scan code mapping and must be sent by virtual key.</summary>
    private static bool IsVkOnly(ushort vk) => vk is >= 0xA6 and <= 0xB7;

    private void AddMouseButtons(MouseButtons buttons, bool up)
    {
        if (buttons == MouseButtons.None) return;
        if ((buttons & MouseButtons.Left) != 0) AddMouse(up ? MouseEventLeftUp : MouseEventLeftDown);
        if ((buttons & MouseButtons.Right) != 0) AddMouse(up ? MouseEventRightUp : MouseEventRightDown);
        if ((buttons & MouseButtons.Middle) != 0) AddMouse(up ? MouseEventMiddleUp : MouseEventMiddleDown);
        if ((buttons & MouseButtons.X1) != 0) AddMouse(up ? MouseEventXUp : MouseEventXDown, XButton1);
        if ((buttons & MouseButtons.X2) != 0) AddMouse(up ? MouseEventXUp : MouseEventXDown, XButton2);
    }

    private void AddMouse(uint flags, uint data = 0, int dx = 0, int dy = 0) =>
        _batch.Add(new NativeInput
        {
            Type = InputMouse,
            Data = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, MouseData = data, Flags = flags, ExtraInfo = Signature } },
        });

    private void Flush()
    {
        if (_batch.Count == 0) return;
        var arr = CollectionsMarshal.AsSpan(_batch).ToArray();
        uint sent = SendInput((uint)arr.Length, arr, InputSize);
        if (sent != arr.Length && _failures++ % 500 == 0)
        {
            // Typically UIPI: the foreground window is elevated and we are not.
            Log.ForContext<KeyboardMouseSink>().Warning(
                "SendInput injected {Sent}/{Total} events (error {Error}). If the game runs as administrator, run reController as administrator too.",
                sent, arr.Length, Marshal.GetLastPInvokeError());
        }
    }

    public void Dispose() => ReleaseAll();
}
