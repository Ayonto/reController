using System.Collections.Concurrent;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.Xbox360.Exceptions;
using ReController.Core.Input;
using Serilog;

namespace ReController.Platform.Windows.Output;

public enum VirtualBusStatus
{
    Unknown,
    Ready,
    NotInstalled,
    VersionMismatch,
    Error,
}

/// <summary>Owns the ViGEmBus connection and every virtual controller reController plugs in.</summary>
internal sealed class VirtualPadHost : IDisposable
{
    private readonly ILogger _log = Log.ForContext<VirtualPadHost>();
    private readonly ConcurrentDictionary<int, byte> _virtualUserIndices = new();
    private ViGEmClient? _client;

    public VirtualBusStatus Status { get; private set; } = VirtualBusStatus.Unknown;
    public string? StatusDetail { get; private set; }

    public bool IsVirtualUserIndex(int index) => _virtualUserIndices.ContainsKey(index);

    public bool EnsureClient()
    {
        if (_client is not null) return true;
        try
        {
            _client = new ViGEmClient();
            Status = VirtualBusStatus.Ready;
            StatusDetail = null;
            _log.Information("Connected to ViGEmBus");
            return true;
        }
        catch (VigemBusNotFoundException ex)
        {
            var installed = Services.DriverInfo.ViGEmBus();
            Set(VirtualBusStatus.NotInstalled, installed.Installed
                ? $"ViGEmBus is registered (v{installed.Version ?? "?"}) but its bus device is missing or outdated. Reinstall the latest ViGEmBus."
                : "ViGEmBus driver is not installed.", ex);
        }
        catch (VigemBusVersionMismatchException ex)
        {
            Set(VirtualBusStatus.VersionMismatch, "Installed ViGEmBus driver version is not supported. Please update it.", ex);
        }
        catch (Exception ex)
        {
            Set(VirtualBusStatus.Error, ex.Message, ex);
        }
        return false;
    }

    private void Set(VirtualBusStatus s, string detail, Exception ex)
    {
        if (Status != s) _log.Error(ex, "ViGEmBus unavailable: {Detail}", detail);
        Status = s;
        StatusDetail = detail;
    }

    public VirtualXbox360? CreateXbox360(Action<byte, byte> onRumble)
    {
        if (!EnsureClient()) return null;
        var pad = _client!.CreateXbox360Controller();
        pad.AutoSubmitReport = false;
        pad.Connect();
        int userIndex = -1;
        // The user index is assigned asynchronously by the XUSB stack.
        for (int attempt = 0; attempt < 40 && userIndex < 0; attempt++)
        {
            try { userIndex = pad.UserIndex; }
            catch (Xbox360UserIndexNotReportedException) { Thread.Sleep(25); }
        }
        if (userIndex >= 0) _virtualUserIndices[userIndex] = 0;
        _log.Information("Virtual Xbox 360 controller plugged in (XInput slot {Slot})", userIndex < 0 ? "?" : userIndex + 1);
        return new VirtualXbox360(pad, userIndex, onRumble, this);
    }

    internal void Release(VirtualXbox360 pad)
    {
        if (pad.UserIndex >= 0) _virtualUserIndices.TryRemove(pad.UserIndex, out _);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        _virtualUserIndices.Clear();
    }
}

internal sealed class VirtualXbox360 : IDisposable
{
    private readonly IXbox360Controller _pad;
    private readonly VirtualPadHost _host;
    private readonly Xbox360FeedbackReceivedEventHandler _feedback;
    private GamepadState _last;
    private bool _hasLast;
    private bool _disposed;

    public VirtualXbox360(IXbox360Controller pad, int userIndex, Action<byte, byte> onRumble, VirtualPadHost host)
    {
        _pad = pad;
        _host = host;
        UserIndex = userIndex;
        _feedback = (_, e) => onRumble(e.LargeMotor, e.SmallMotor);
        _pad.FeedbackReceived += _feedback;
    }

    public int UserIndex { get; }

    public void Submit(in GamepadState s)
    {
        if (_disposed || (_hasLast && s == _last)) return;
        _last = s;
        _hasLast = true;

        ushort w = 0;
        var b = s.Buttons;
        if ((b & GamepadButtons.DPadUp) != 0) w |= 0x0001;
        if ((b & GamepadButtons.DPadDown) != 0) w |= 0x0002;
        if ((b & GamepadButtons.DPadLeft) != 0) w |= 0x0004;
        if ((b & GamepadButtons.DPadRight) != 0) w |= 0x0008;
        if ((b & GamepadButtons.Start) != 0) w |= 0x0010;
        if ((b & GamepadButtons.Back) != 0) w |= 0x0020;
        if ((b & GamepadButtons.LeftStick) != 0) w |= 0x0040;
        if ((b & GamepadButtons.RightStick) != 0) w |= 0x0080;
        if ((b & GamepadButtons.LeftBumper) != 0) w |= 0x0100;
        if ((b & GamepadButtons.RightBumper) != 0) w |= 0x0200;
        if ((b & GamepadButtons.Guide) != 0) w |= 0x0400;
        if ((b & GamepadButtons.A) != 0) w |= 0x1000;
        if ((b & GamepadButtons.B) != 0) w |= 0x2000;
        if ((b & GamepadButtons.X) != 0) w |= 0x4000;
        if ((b & GamepadButtons.Y) != 0) w |= 0x8000;

        _pad.SetButtonsFull(w);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, Axis(s.LeftX));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, Axis(s.LeftY));
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, Axis(s.RightX));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, Axis(s.RightY));
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)Math.Round(Math.Clamp(s.LeftTrigger, 0, 1) * 255));
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, (byte)Math.Round(Math.Clamp(s.RightTrigger, 0, 1) * 255));
        _pad.SubmitReport();
    }

    private static short Axis(float v) => (short)Math.Round(Math.Clamp(v, -1f, 1f) * 32767f);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pad.FeedbackReceived -= _feedback;
        try
        {
            _pad.ResetReport();
            _pad.SubmitReport();
            _pad.Disconnect();
        }
        catch (Exception ex)
        {
            Log.ForContext<VirtualXbox360>().Warning(ex, "Error while unplugging virtual controller");
        }
        _host.Release(this);
    }
}
