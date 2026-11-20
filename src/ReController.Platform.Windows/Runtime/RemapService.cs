using System.Collections.Concurrent;
using System.Diagnostics;
using ReController.Core.Input;
using ReController.Core.Mapping;
using ReController.Platform.Windows.Input;
using ReController.Platform.Windows.Output;
using Serilog;
using static ReController.Platform.Windows.Native.Win32;

namespace ReController.Platform.Windows.Runtime;

public sealed record DeviceSnapshot(PhysicalDeviceInfo Info, int ActiveSlot, bool VirtualPadActive, int VirtualSlot);

public readonly record struct LiveState(
    GamepadState Raw,
    GamepadState Output,
    int ActiveLayer,
    int ActiveSlot,
    bool Remapping,
    bool Connected);

/// <summary>
/// The remapping runtime. Runs a dedicated, time-critical engine thread that polls every controller,
/// runs each one's <see cref="MappingEngine"/>, drives virtual pads and keyboard/mouse output.
/// All public members are thread-safe; work is marshalled to the engine thread via a command queue.
/// </summary>
public sealed class RemapService : IDisposable
{
    public const int SlotCount = 4;
    private const double EnumerateInterval = 0.5;

    private readonly ILogger _log = Log.ForContext<RemapService>();
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MappingConfig?[]> _assignments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _activeSlots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LiveBox> _live = new(StringComparer.Ordinal);
    private readonly List<IInputProvider> _providers = [];
    private readonly VirtualPadHost _vigem = new();
    private readonly KeyboardMouseSink _kbm = new();
    private readonly OutputFrame _merged = new();
    private readonly Thread _thread;
    private volatile bool _stop;
    private volatile bool _enabled;
    private volatile int _pollingRate = 1000;
    private volatile Control _slotModifier = Control.Guide;
    private IReadOnlyList<DeviceSnapshot> _devices = [];
    private double _measuredRate;
    private string? _lastProblem;

    public RemapService()
    {
        _thread = new Thread(Run) { Name = "reController engine", IsBackground = true, Priority = ThreadPriority.Highest };
    }

    /// <summary>Raised on the engine thread whenever the device list or a device's slot/virtual state changes.</summary>
    public event Action<IReadOnlyList<DeviceSnapshot>>? DevicesChanged;

    /// <summary>Raised on the engine thread when a non-fatal runtime problem occurs (shown to the user).</summary>
    public event Action<string>? Problem;

    public IReadOnlyList<DeviceSnapshot> Devices => Volatile.Read(ref _devices);
    public bool Enabled => _enabled;
    public VirtualBusStatus VirtualBusStatus => _vigem.Status;
    public string? VirtualBusDetail => _vigem.StatusDetail;
    public double MeasuredPollingRate => Volatile.Read(ref _measuredRate);

    public void Start()
    {
        if (!_thread.IsAlive) _thread.Start();
    }

    public void SetEnabled(bool enabled) => Post(() =>
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (enabled) _vigem.EnsureClient();
        foreach (var s in _sessions.Values) s.ResetEngines();
        if (!enabled)
        {
            foreach (var s in _sessions.Values) s.DisposeVirtual();
            _kbm.ReleaseAll();
        }
        _log.Information("Remapping {State}", enabled ? "enabled" : "disabled");
        PublishDevices();
    });

    public void SetPollingRate(int hz) => _pollingRate = Math.Clamp(hz, 125, 1000);

    public void SetSlotSwitchModifier(Control modifier) => _slotModifier = modifier;

    /// <summary>Assigns (a private copy of) a config to a device slot. Null clears the slot.</summary>
    public void AssignSlot(string deviceKey, int slot, MappingConfig? config, bool activate)
    {
        var copy = config?.Clone();
        Post(() =>
        {
            if (!_assignments.TryGetValue(deviceKey, out var slots)) _assignments[deviceKey] = slots = new MappingConfig?[SlotCount];
            slots[Math.Clamp(slot, 0, SlotCount - 1)] = copy;
            if (activate) _activeSlots[deviceKey] = Math.Clamp(slot, 0, SlotCount - 1);
            if (_sessions.TryGetValue(deviceKey, out var s)) s.Rebuild(slots, ActiveSlotFor(deviceKey));
            PublishDevices();
        });
    }

    public void SetActiveSlot(string deviceKey, int slot) => Post(() =>
    {
        _activeSlots[deviceKey] = Math.Clamp(slot, 0, SlotCount - 1);
        if (_sessions.TryGetValue(deviceKey, out var s)) s.SwitchSlot(ActiveSlotFor(deviceKey));
        PublishDevices();
    });

    /// <summary>Hot-reloads a config that is being edited, wherever it is currently assigned.</summary>
    public void UpdateConfig(MappingConfig config)
    {
        var copy = config.Clone();
        Post(() =>
        {
            foreach (var (key, slots) in _assignments)
            {
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i]?.Id == copy.Id) { slots[i] = copy.Clone(); changed = true; }
                }
                if (changed && _sessions.TryGetValue(key, out var s)) s.Rebuild(slots, ActiveSlotFor(key));
            }
        });
    }

    /// <summary>Call after drivers were (re)installed: retries the ViGEmBus connection right away.</summary>
    public void RetryVirtualBus() => Post(() =>
    {
        _lastProblem = null;
        if (_enabled) _vigem.EnsureClient();
        foreach (var s in _sessions.Values) s.RetryVirtualAt = 0;
    });

    public bool TryGetLive(string deviceKey, out LiveState state)
    {
        if (_live.TryGetValue(deviceKey, out var box))
        {
            lock (box) state = box.State;
            return true;
        }
        state = default;
        return false;
    }

    private int ActiveSlotFor(string key) => _activeSlots.TryGetValue(key, out int s) ? s : 0;

    private void Post(Action a) => _commands.Enqueue(a);

    // ------------------------------------------------------------------------------------------
    // Engine thread
    // ------------------------------------------------------------------------------------------

    private void Run()
    {
        uint taskIndex = 0;
        IntPtr mmcss = IntPtr.Zero;
        try { mmcss = AvSetMmThreadCharacteristics("Games", ref taskIndex); } catch (DllNotFoundException) { }

        _providers.Add(new XInputProvider(_vigem.IsVirtualUserIndex));
        _providers.Add(new SdlInputProvider());
        foreach (var p in _providers)
        {
            try { p.Initialize(); }
            catch (Exception ex) { _log.Error(ex, "Failed to initialise {Provider}", p.Name); }
        }

        using var timer = new PrecisionTimer();
        var clock = Stopwatch.StartNew();
        double lastEnumerate = -1;
        double rateWindowStart = 0;
        int rateTicks = 0;
        var found = new List<PhysicalDeviceInfo>();
        int consecutiveErrors = 0;

        while (!_stop)
        {
            double now = clock.Elapsed.TotalSeconds;
            try
            {
                while (_commands.TryDequeue(out var cmd)) cmd();

                if (now - lastEnumerate >= EnumerateInterval)
                {
                    lastEnumerate = now;
                    found.Clear();
                    foreach (var p in _providers) if (p.IsAvailable) p.Enumerate(found);
                    SyncSessions(found);
                }

                foreach (var p in _providers) if (p.IsAvailable) p.Update();

                _merged.Clear();
                foreach (var s in _sessions.Values) Tick(s, now);
                if (_enabled) _kbm.Apply(_merged);

                consecutiveErrors = 0;
            }
            catch (Exception ex)
            {
                // Never let the engine thread die; but don't spin hot on a persistent fault either.
                if (consecutiveErrors++ % 1000 == 0) _log.Error(ex, "Engine tick failed");
                if (consecutiveErrors > 50) Thread.Sleep(50);
            }

            rateTicks++;
            if (now - rateWindowStart >= 1)
            {
                Volatile.Write(ref _measuredRate, rateTicks / (now - rateWindowStart));
                rateTicks = 0;
                rateWindowStart = now;
            }

            timer.WaitNext(1.0 / _pollingRate);
        }

        // Shutdown: release everything we hold.
        try { _kbm.ReleaseAll(); } catch (Exception ex) { _log.Warning(ex, "Releasing keys on shutdown failed"); }
        foreach (var s in _sessions.Values) s.DisposeVirtual();
        _sessions.Clear();
        foreach (var p in _providers) { try { p.Dispose(); } catch (Exception ex) { _log.Warning(ex, "Provider dispose failed"); } }
        _vigem.Dispose();
        if (mmcss != IntPtr.Zero) AvRevertMmThreadCharacteristics(mmcss);
    }

    private void Tick(Session s, double now)
    {
        bool connected = s.Provider.TryRead(s.Info.Key, out var raw);
        if (!connected) raw = default;

        // Slot switching: hold modifier + D-Pad Up/Right/Down/Left => slot 1/2/3/4.
        var mod = _slotModifier;
        if (_enabled && mod != Control.None && ControlInfo.IsDigitalButton(mod) && raw.IsDown(ControlInfo.ToButtonFlag(mod)))
        {
            int requested = raw.IsDown(GamepadButtons.DPadUp) ? 0
                : raw.IsDown(GamepadButtons.DPadRight) ? 1
                : raw.IsDown(GamepadButtons.DPadDown) ? 2
                : raw.IsDown(GamepadButtons.DPadLeft) ? 3 : -1;
            if (requested >= 0 && requested != s.ActiveSlot && s.Engines[requested] is not null && !s.SlotComboLatched)
            {
                s.SlotComboLatched = true;
                _activeSlots[s.Info.Key] = requested;
                s.SwitchSlot(requested);
                _log.Information("{Device}: switched to slot {Slot}", s.Info.Name, requested + 1);
                PublishDevices();
            }
        }
        else
        {
            s.SlotComboLatched = false;
        }

        var engine = s.ActiveEngine;
        GamepadState output = default;
        int layer = 0;
        bool remapping = _enabled && engine is not null && connected;

        if (remapping)
        {
            engine!.Process(raw, now, s.Frame);
            output = s.Frame.Pad;
            layer = s.Frame.ActiveLayer;

            if (engine.Config.Output == VirtualOutput.Xbox360)
            {
                if (s.Virtual is null && now >= s.RetryVirtualAt)
                {
                    s.Virtual = TryCreateVirtual(s);
                    if (s.Virtual is null) s.RetryVirtualAt = now + 5;
                    PublishDevices();
                }
                s.Virtual?.Submit(output);
            }
            else if (s.Virtual is not null)
            {
                s.DisposeVirtual();
                PublishDevices();
            }

            foreach (var k in s.Frame.Keys) _merged.Keys.Add(k);
            _merged.MouseButtons |= s.Frame.MouseButtons;
            _merged.MouseDx += s.Frame.MouseDx;
            _merged.MouseDy += s.Frame.MouseDy;
            _merged.WheelVertical += s.Frame.WheelVertical;
            _merged.WheelHorizontal += s.Frame.WheelHorizontal;
        }
        else if (s.Virtual is not null && (!_enabled || engine is null))
        {
            s.DisposeVirtual();
            PublishDevices();
        }
        else
        {
            s.Virtual?.Submit(default); // controller dropped out: neutralise but keep the pad plugged
        }

        // Updated in place (no per-tick allocation); the UI copies it under the same uncontended lock.
        lock (s.Live) s.Live.State = new LiveState(raw, output, layer, s.ActiveSlot, remapping, connected);
    }

    private VirtualXbox360? TryCreateVirtual(Session s)
    {
        try
        {
            var pad = _vigem.CreateXbox360((large, small) =>
            {
                if (s.ActiveEngine?.Config.ForwardRumble == true) s.Provider.SetRumble(s.Info.Key, large / 255f, small / 255f);
            });
            if (pad is null) Report(_vigem.StatusDetail ?? "Virtual controller driver unavailable.");
            else _lastProblem = null;
            return pad;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not create virtual controller for {Device}", s.Info.Name);
            Report($"Could not create a virtual controller: {ex.Message}");
            return null;
        }
    }

    /// <summary>Raises <see cref="Problem"/> once per distinct message so retries don't spam the user.</summary>
    private void Report(string message)
    {
        if (message == _lastProblem) return;
        _lastProblem = message;
        try { Problem?.Invoke(message); }
        catch (Exception ex) { _log.Error(ex, "Problem handler failed"); }
    }

    private void SyncSessions(List<PhysicalDeviceInfo> found)
    {
        bool changed = false;
        foreach (var info in found)
        {
            if (_sessions.ContainsKey(info.Key)) continue;
            var provider = _providers.First(p => p.Name == info.Provider);
            var s = new Session(info, provider);
            if (_assignments.TryGetValue(info.Key, out var slots)) s.Rebuild(slots, ActiveSlotFor(info.Key));
            _sessions[info.Key] = s;
            _live[info.Key] = s.Live;
            _log.Information("Device online: {Name} [{Key}]", info.Name, info.Key);
            changed = true;
        }
        foreach (var key in _sessions.Keys.Where(k => !found.Exists(f => f.Key == k)).ToList())
        {
            _sessions[key].DisposeVirtual();
            _sessions.Remove(key);
            _live.TryRemove(key, out _);
            _log.Information("Device offline: {Key}", key);
            changed = true;
        }
        if (changed) PublishDevices();
    }

    private void PublishDevices()
    {
        var list = _sessions.Values
            .Select(s => new DeviceSnapshot(s.Info, s.ActiveSlot, s.Virtual is not null, s.Virtual?.UserIndex ?? -1))
            .OrderBy(d => d.Info.Key, StringComparer.Ordinal)
            .ToList();
        Volatile.Write(ref _devices, list);
        try { DevicesChanged?.Invoke(list); }
        catch (Exception ex) { _log.Error(ex, "DevicesChanged handler failed"); }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(3)))
            _log.Warning("Engine thread did not stop in time");
        _kbm.Dispose();
    }

    private sealed class LiveBox
    {
        public LiveState State;
    }

    private sealed class Session(PhysicalDeviceInfo info, IInputProvider provider)
    {
        public PhysicalDeviceInfo Info { get; } = info;
        public IInputProvider Provider { get; } = provider;
        public MappingEngine?[] Engines { get; } = new MappingEngine?[SlotCount];
        public OutputFrame Frame { get; } = new();
        public int ActiveSlot { get; private set; }
        public VirtualXbox360? Virtual { get; set; }
        public double RetryVirtualAt { get; set; }
        public bool SlotComboLatched { get; set; }
        public LiveBox Live { get; } = new();

        public MappingEngine? ActiveEngine => Engines[ActiveSlot];

        public void Rebuild(MappingConfig?[] slots, int active)
        {
            for (int i = 0; i < SlotCount; i++) Engines[i] = slots[i] is { } c ? new MappingEngine(c.Clone()) : null;
            ActiveSlot = active;
        }

        public void SwitchSlot(int slot)
        {
            ActiveSlot = slot;
            ResetEngines();
        }

        public void ResetEngines()
        {
            foreach (var e in Engines) e?.Reset();
        }

        public void DisposeVirtual()
        {
            Virtual?.Dispose();
            Virtual = null;
            RetryVirtualAt = 0;
        }
    }
}
