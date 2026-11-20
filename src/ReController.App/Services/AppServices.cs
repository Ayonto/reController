using System.Windows.Threading;
using ReController.App.Infrastructure;
using ReController.Core.Input;
using ReController.Core.Mapping;
using ReController.Core.Profiles;
using ReController.Core.Settings;
using Serilog;

namespace ReController.App.Services;

/// <summary>Owns <see cref="AppSettings"/> and persists it with a short debounce.</summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store = new(AppPaths.SettingsFile);
    private readonly DispatcherTimer _debounce;

    public SettingsService()
    {
        Current = _store.Load();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _debounce.Tick += (_, _) => SaveNow();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void SaveNow()
    {
        _debounce.Stop();
        try { _store.Save(Current); }
        catch (Exception ex) { Log.Error(ex, "Failed to save settings"); }
    }
}

/// <summary>Loads game profiles and saves edited ones in the background (debounced, crash-safe).</summary>
public sealed class ProfileService
{
    private readonly ProfileStore _store = new(AppPaths.Profiles);
    private readonly HashSet<GameProfile> _dirty = [];
    private readonly DispatcherTimer _debounce;

    public ProfileService()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) => Flush();
    }

    /// <summary>True while edits are waiting to be written.</summary>
    public event Action<bool>? PendingChanged;

    public string Directory => _store.Directory;

    public List<GameProfile> LoadAll(out List<ProfileLoadIssue> issues)
    {
        var (profiles, problems) = _store.LoadAll();
        issues = problems;
        foreach (var i in problems) Log.Warning("Profile {File} could not be loaded and was quarantined: {Message}", i.File, i.Message);
        if (profiles.Count == 0)
        {
            profiles.AddRange(DefaultProfiles.Create());
            foreach (var p in profiles) _store.Save(p);
            profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }
        return profiles;
    }

    public void MarkDirty(GameProfile p)
    {
        _dirty.Add(p);
        PendingChanged?.Invoke(true);
        _debounce.Stop();
        _debounce.Start();
    }

    public void Flush()
    {
        _debounce.Stop();
        foreach (var p in _dirty.ToList())
        {
            try
            {
                _store.Save(p);
                _dirty.Remove(p);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save profile {Name}", p.Name);
            }
        }
        PendingChanged?.Invoke(_dirty.Count > 0);
    }

    public void Delete(GameProfile p)
    {
        _dirty.Remove(p);
        _store.Delete(p.Id);
    }
}

/// <summary>Profiles created on first run so the app is useful immediately.</summary>
public static class DefaultProfiles
{
    public static IEnumerable<GameProfile> Create()
    {
        var general = GameProfile.Create("General");
        general.Configs[0].Name = "Passthrough";
        var fix = MappingConfig.CreateDefault("Broken button fix");
        // Example: a dead A button replaced by the right bumper (edit to taste).
        fix.Layers[0].Set(new ButtonBinding { Source = Control.RightBumper, Actions = [new GamepadAction(Control.A)] });
        general.Configs.Add(fix);
        yield return general;

        var desktop = GameProfile.Create("Desktop");
        var d = desktop.Configs[0];
        d.Name = "Mouse & keyboard";
        d.Output = VirtualOutput.None;
        d.LeftStick.Mode = StickMode.Mouse;
        d.LeftStick.Curve = 1.6;
        d.RightStick.Mode = StickMode.Disabled;
        void Bind(Control c, MappingAction a) => d.Layers[0].Set(new ButtonBinding { Source = c, Actions = [a] });
        Bind(Control.A, new MouseButtonAction(MouseButton.Left));
        Bind(Control.B, new MouseButtonAction(MouseButton.Right));
        Bind(Control.X, new KeyAction(0x0D)); // Enter
        Bind(Control.Y, new KeyAction(0x1B)); // Esc
        Bind(Control.LeftBumper, new MouseButtonAction(MouseButton.X1));
        Bind(Control.RightBumper, new MouseButtonAction(MouseButton.X2));
        Bind(Control.RightStickUp, new MouseWheelAction(WheelDirection.Up));
        Bind(Control.RightStickDown, new MouseWheelAction(WheelDirection.Down));
        Bind(Control.DPadUp, new KeyAction(0x26));
        Bind(Control.DPadDown, new KeyAction(0x28));
        Bind(Control.DPadLeft, new KeyAction(0x25));
        Bind(Control.DPadRight, new KeyAction(0x27));
        Bind(Control.Start, new KeyAction(0x5B)); // Win
        Bind(Control.Back, new KeyAction(0x09)); // Tab
        d.RightStick.Mode = StickMode.Gamepad; // directions are bound to the wheel above
        yield return desktop;
    }
}
