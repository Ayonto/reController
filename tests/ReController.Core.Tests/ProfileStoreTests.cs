using ReController.Core.Input;
using ReController.Core.Mapping;
using ReController.Core.Profiles;

namespace ReController.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rc-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Round_trips_all_action_types()
    {
        var store = new ProfileStore(_dir);
        var p = GameProfile.Create("Test Game");
        p.Executables.Add(@"C:\Games\test.exe");
        var cfg = p.Configs[0];
        cfg.Layers[0].Set(new ButtonBinding
        {
            Source = Control.A,
            Mode = ActivationMode.Turbo,
            TurboRateHz = 15,
            Actions = [new GamepadAction(Control.B), new KeyAction(0x41), new MouseButtonAction(MouseButton.X1), new MouseWheelAction(WheelDirection.Up)],
        });
        cfg.Layers[0].Set(new ButtonBinding { Source = Control.Back, Actions = [new ShiftAction(2)] });
        cfg.RightStick.Mode = StickMode.Mouse;
        store.Save(p);

        var (loaded, issues) = store.LoadAll();
        Assert.Empty(issues);
        var l = Assert.Single(loaded);
        Assert.Equal("Test Game", l.Name);
        Assert.Equal("test.exe", Assert.Single(l.Executables));
        var b = l.Configs[0].Layers[0].Find(Control.A)!;
        Assert.Equal(ActivationMode.Turbo, b.Mode);
        Assert.Equal(4, b.Actions.Count);
        Assert.IsType<MouseWheelAction>(b.Actions[3]);
        Assert.IsType<ShiftAction>(l.Configs[0].Layers[0].Find(Control.Back)!.Actions[0]);
        Assert.Equal(StickMode.Mouse, l.Configs[0].RightStick.Mode);
        Assert.Equal(MappingConfig.MaxShiftLayers + 1, l.Configs[0].Layers.Count);
    }

    [Fact]
    public void Corrupt_files_are_quarantined_not_fatal()
    {
        var store = new ProfileStore(_dir);
        File.WriteAllText(Path.Combine(_dir, "bad" + ProfileStore.FileExtension), "{ not json");
        store.Save(GameProfile.Create("Good"));

        var (loaded, issues) = store.LoadAll();
        Assert.Single(loaded);
        Assert.Single(issues);
        Assert.Contains(Directory.GetFiles(_dir), f => f.EndsWith(".corrupt", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_assigns_fresh_ids()
    {
        var p = GameProfile.Create("Share me");
        string path = Path.Combine(_dir, "share.rcprofile");
        Directory.CreateDirectory(_dir);
        ProfileStore.Export(p, path);
        var imported = ProfileStore.Import(path);
        Assert.NotEqual(p.Id, imported.Id);
        Assert.NotEqual(p.Configs[0].Id, imported.Configs[0].Id);
    }

    [Fact]
    public void Newer_schema_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() => ProfileStore.Deserialize("{\"schemaVersion\": 99, \"name\": \"x\"}"));
    }
}
