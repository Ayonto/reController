using System.Windows;
using System.Windows.Media;

namespace ReController.App.Infrastructure;

public sealed record AccentPreset(string Name, Color Color, Color OnAccent);

public static class ThemeManager
{
    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("Cyan", Color.FromRgb(0x3D, 0xD6, 0xF5), Color.FromRgb(0x06, 0x10, 0x15)),
        new("Lime", Color.FromRgb(0xB8, 0xF3, 0x4A), Color.FromRgb(0x10, 0x16, 0x05)),
        new("Amber", Color.FromRgb(0xFF, 0xC2, 0x4B), Color.FromRgb(0x1A, 0x10, 0x00)),
        new("Violet", Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0x0E, 0x08, 0x1F)),
        new("Coral", Color.FromRgb(0xFF, 0x7A, 0x6B), Color.FromRgb(0x1F, 0x07, 0x05)),
        new("Mint", Color.FromRgb(0x4F, 0xF0, 0xB5), Color.FromRgb(0x03, 0x17, 0x0F)),
    ];

    public static AccentPreset Resolve(string? name) =>
        Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];

    public static void Apply(string? name)
    {
        var preset = Resolve(name);
        var res = Application.Current.Resources;
        var c = preset.Color;
        res["AccentColor"] = c;
        res["AccentBrush"] = Freeze(new SolidColorBrush(c));
        res["AccentSoftBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0x2E, c.R, c.G, c.B)));
        res["AccentFaintBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0x12, c.R, c.G, c.B)));
        res["AccentGlowBrush"] = Freeze(new RadialGradientBrush(Color.FromArgb(0x22, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)));
        res["OnAccentBrush"] = Freeze(new SolidColorBrush(preset.OnAccent));
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
