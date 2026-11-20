using ReController.Core.Mapping;

namespace ReController.Core.Tests;

public class AnalogProcessingTests
{
    [Fact]
    public void Inside_deadzone_is_zero()
    {
        var (x, y) = AnalogProcessing.ShapeStick(0.05f, 0.05f, new StickSettings { Deadzone = 0.1 });
        Assert.Equal(0f, x);
        Assert.Equal(0f, y);
    }

    [Fact]
    public void Deadzone_is_rescaled_so_full_deflection_still_reaches_one()
    {
        var (x, _) = AnalogProcessing.ShapeStick(1f, 0f, new StickSettings { Deadzone = 0.2 });
        Assert.Equal(1f, x, 4);
    }

    [Fact]
    public void Direction_is_preserved()
    {
        var (x, y) = AnalogProcessing.ShapeStick(0.3f, 0.4f, new StickSettings { Deadzone = 0.1, Curve = 2 });
        Assert.Equal(0.75f, x / y, 3);
    }

    [Fact]
    public void Anti_deadzone_sets_floor()
    {
        var (x, _) = AnalogProcessing.ShapeStick(0.11f, 0f, new StickSettings { Deadzone = 0.1, AntiDeadzone = 0.25 });
        Assert.True(x >= 0.25f);
    }

    [Fact]
    public void Inversion_flips_axes()
    {
        var (x, y) = AnalogProcessing.ShapeStick(0.5f, 0.5f, new StickSettings { Deadzone = 0, InvertX = true, InvertY = true });
        Assert.True(x < 0 && y < 0);
    }

    [Theory]
    [InlineData(0.01f, false, 0f)]
    [InlineData(1f, false, 1f)]
    [InlineData(0.3f, true, 1f)]
    public void Trigger_shaping(float raw, bool hair, float expected)
    {
        Assert.Equal(expected, AnalogProcessing.ShapeTrigger(raw, new TriggerSettings { Deadzone = 0.02, HairTrigger = hair }), 3);
    }
}
