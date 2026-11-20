using System.Text.Json;
using ReController.Core.Input;
using ReController.Core.Profiles;

namespace ReController.Core.Settings;

public sealed class SlotAssignment
{
    public Guid GameId { get; set; }
    public Guid ConfigId { get; set; }
}

/// <summary>Per-controller state remembered across sessions (keyed by a stable device key).</summary>
public sealed class DeviceAssignment
{
    public const int SlotCount = 4;

    public string DeviceKey { get; set; } = "";
    public string? Alias { get; set; }
    public int ActiveSlot { get; set; }
    public List<SlotAssignment?> Slots { get; set; } = [];

    public void Normalize()
    {
        Slots ??= [];
        while (Slots.Count < SlotCount) Slots.Add(null);
        if (Slots.Count > SlotCount) Slots.RemoveRange(SlotCount, Slots.Count - SlotCount);
        ActiveSlot = Math.Clamp(ActiveSlot, 0, SlotCount - 1);
    }
}

public sealed class WindowPlacement
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 760;
    public bool Maximized { get; set; }
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Accent { get; set; } = "Cyan";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool RemapEnabled { get; set; }
    public int PollingRateHz { get; set; } = 1000;
    public bool AutoProfileSwitching { get; set; } = true;

    /// <summary>Hold this + D-Pad Up/Right/Down/Left to switch to slot 1/2/3/4. <see cref="Control.None"/> disables.</summary>
    public Control SlotSwitchModifier { get; set; } = Control.Guide;

    public bool ShowLiveInput { get; set; } = true;
    public Guid? LastGameId { get; set; }
    public string? LastDeviceKey { get; set; }
    public WindowPlacement Window { get; set; } = new();
    public List<DeviceAssignment> Devices { get; set; } = [];
    public bool FirstRunCompleted { get; set; }

    public static readonly int[] SupportedPollingRates = [250, 500, 1000];

    public DeviceAssignment GetOrCreateDevice(string key)
    {
        var d = Devices.Find(x => x.DeviceKey == key);
        if (d is null)
        {
            d = new DeviceAssignment { DeviceKey = key };
            Devices.Add(d);
        }
        d.Normalize();
        return d;
    }

    public void Normalize()
    {
        if (!SupportedPollingRates.Contains(PollingRateHz)) PollingRateHz = 1000;
        Window ??= new();
        Devices ??= [];
        Devices.RemoveAll(d => d is null || string.IsNullOrEmpty(d.DeviceKey));
        foreach (var d in Devices) d.Normalize();
        if (string.IsNullOrWhiteSpace(Accent)) Accent = "Cyan";
    }
}

public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonDefaults.Options);
                if (s is not null)
                {
                    s.Normalize();
                    return s;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            try { File.Copy(Path, Path + ".corrupt", overwrite: true); } catch (IOException) { }
        }
        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
    }

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonDefaults.Options));
}
