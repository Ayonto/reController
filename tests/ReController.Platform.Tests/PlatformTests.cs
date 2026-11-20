using ReController.Core.Input;
using ReController.Platform.Windows.Input;
using ReController.Platform.Windows.Native;
using ReController.Platform.Windows.Services;

namespace ReController.Platform.Tests;

public class HidHideParsingTests
{
    private const string GamingJson = """
    [
      {
        "friendlyName": "Xbox 360 Controller for Windows",
        "devices": [
          { "present": true, "gamingDevice": true, "deviceInstancePath": "USB\\VID_045E&PID_028E\\1234", "description": "Xbox 360 Controller" },
          { "present": false, "gamingDevice": true, "deviceInstancePath": "HID\\VID_045E&PID_028E&IG_00\\5678" }
        ]
      },
      {
        "friendlyName": "DualSense Wireless Controller",
        "devices": [ { "present": true, "deviceInstancePath": "HID\\VID_054C&PID_0CE6\\abcd" } ]
      }
    ]
    """;

    [Fact]
    public void Parses_gaming_devices_with_group_names()
    {
        var devices = HidHideClient.ParseGamingDevices(GamingJson);
        Assert.Equal(3, devices.Count);
        Assert.Equal("Xbox 360 Controller for Windows", devices[0].Name);
        Assert.False(devices[1].Present);
        Assert.Equal("DualSense Wireless Controller", devices[2].Name);
    }

    [Fact]
    public void Garbage_input_yields_empty_list()
    {
        Assert.Empty(HidHideClient.ParseGamingDevices("not json"));
        Assert.Empty(HidHideClient.ParseGamingDevices(""));
    }

    [Fact]
    public void Parses_quoted_list_output()
    {
        var paths = HidHideClient.ParseQuoted("--dev-hide \"HID\\VID_1\"\r\n--dev-hide \"USB\\VID_2\"\r\n");
        Assert.Equal([@"HID\VID_1", @"USB\VID_2"], paths);
    }
}

public class ElevatedBatchTests
{
    [Theory]
    [InlineData("--format-c")]
    [InlineData("cmd.exe")]
    public async Task Rejects_commands_outside_allow_list(string verb) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => ElevatedBatch.RunHidHideAsync([[verb]]));

    [Fact]
    public async Task Rejects_quote_injection_in_arguments() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => ElevatedBatch.RunHidHideAsync([["--dev-hide", "x\" --cloak-off \""]]));
}

public class XInputConversionTests
{
    [Fact]
    public void Converts_buttons_axes_and_triggers()
    {
        var g = new XInputNative.XInputGamepad
        {
            Buttons = XInputNative.A | XInputNative.Guide | XInputNative.DPadLeft,
            LeftTrigger = 255,
            RightTrigger = 0,
            ThumbLX = short.MinValue,
            ThumbLY = short.MaxValue,
        };
        var s = XInputProvider.Convert(g);
        Assert.Equal(GamepadButtons.A | GamepadButtons.Guide | GamepadButtons.DPadLeft, s.Buttons);
        Assert.Equal(1f, s.LeftTrigger);
        Assert.Equal(-1f, s.LeftX);
        Assert.Equal(1f, s.LeftY);
    }
}
