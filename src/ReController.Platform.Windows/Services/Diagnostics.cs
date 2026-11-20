using System.Diagnostics;
using System.Text;
using ReController.Core.Input;
using ReController.Core.Mapping;
using ReController.Platform.Windows.Input;
using ReController.Platform.Windows.Native;
using ReController.Platform.Windows.Output;

namespace ReController.Platform.Windows.Services;

public sealed record DiagnosticResult(string Check, bool Ok, string Detail, bool Critical = true);

/// <summary>
/// End-to-end self test used by <c>reController --selftest</c> (and for bug reports).
/// It plugs in a real virtual Xbox 360 pad, drives it through the mapping engine and reads it back via XInput,
/// proving the output path works without needing a physical controller.
/// Must not run while the main engine is running (it initialises and shuts down SDL itself).
/// </summary>
public static class Diagnostics
{
    public static IReadOnlyList<DiagnosticResult> Run()
    {
        var results = new List<DiagnosticResult>
        {
            new("Operating system", true, Environment.OSVersion.VersionString + (Environment.Is64BitProcess ? " (x64)" : " (x86)"), Critical: false),
            new("Elevated", true, ElevatedBatch.IsElevated ? "yes" : "no", Critical: false),
        };

        // XInput
        var xinput = new XInputProvider(_ => false);
        try
        {
            xinput.Initialize();
            var found = new List<PhysicalDeviceInfo>();
            xinput.Enumerate(found); // the provider debounces hot-plug: a slot is reported on its second scan
            found.Clear();
            xinput.Enumerate(found);
            results.Add(new("XInput", xinput.IsAvailable, xinput.IsAvailable ? $"{found.Count} controller(s) connected" : "xinput1_4.dll unavailable"));
        }
        catch (Exception ex)
        {
            results.Add(new("XInput", false, ex.Message));
        }

        // SDL
        var sdl = new SdlInputProvider();
        try
        {
            sdl.Initialize();
            var found = new List<PhysicalDeviceInfo>();
            if (sdl.IsAvailable) sdl.Enumerate(found);
            results.Add(new("SDL3 (PlayStation / Nintendo / generic)", sdl.IsAvailable,
                sdl.IsAvailable ? $"{found.Count} controller(s): {string.Join(", ", found.Select(f => f.Name))}".TrimEnd(':', ' ') : "SDL3.dll could not be loaded", Critical: false));
        }
        catch (Exception ex)
        {
            results.Add(new("SDL3", false, ex.Message, Critical: false));
        }
        finally
        {
            sdl.Dispose();
        }

        // Drivers
        bool bundled = DriverSetup.IsBundled(DriverSetup.ViGEmBus) && DriverSetup.IsBundled(DriverSetup.HidHide);
        results.Add(new("Bundled driver installers", bundled,
            bundled ? $"ViGEmBus {DriverSetup.ViGEmBus.Version}, HidHide {DriverSetup.HidHide.Version}" : "not included in this build", Critical: false));
        var vigemState = DriverInfo.ViGEmBus();
        results.Add(new("ViGEmBus driver", vigemState.Installed, vigemState.Installed ? $"installed {vigemState.Version}" : "not installed"));
        var hidhide = DriverInfo.HidHide();
        results.Add(new("HidHide driver", hidhide.Installed,
            hidhide.Installed ? $"installed {hidhide.Version}; CLI: {HidHideClient.FindCli() ?? "not found"}" : "not installed (recommended)", Critical: false));

        // Virtual pad round trip through the mapping engine.
        using var host = new VirtualPadHost();
        if (!host.EnsureClient())
        {
            results.Add(new("Virtual controller", false, host.StatusDetail ?? "ViGEmBus unavailable"));
            return results;
        }

        VirtualXbox360? pad = null;
        try
        {
            pad = host.CreateXbox360((_, _) => { });
            if (pad is null || pad.UserIndex < 0)
            {
                results.Add(new("Virtual controller", false, "plugged in but no XInput slot was reported"));
                return results;
            }
            results.Add(new("Virtual controller", true, $"plugged in as XInput slot {pad.UserIndex + 1}"));

            // Physical "RB" remapped to "A" + a right-trigger pull, as a user would configure it.
            var config = MappingConfig.CreateDefault("selftest");
            config.Layers[0].Set(new ButtonBinding { Source = Control.RightBumper, Actions = [new GamepadAction(Control.A)] });
            var engine = new MappingEngine(config);
            var frame = new OutputFrame();
            engine.Process(new GamepadState { Buttons = GamepadButtons.RightBumper, LeftX = 0.5f, RightTrigger = 1f }, 0, frame);
            pad.Submit(frame.Pad);

            bool ok = WaitFor(pad.UserIndex, s => (s.Gamepad.Buttons & XInputNative.A) != 0
                                                  && (s.Gamepad.Buttons & XInputNative.RightShoulder) == 0
                                                  && s.Gamepad.RightTrigger > 250
                                                  && s.Gamepad.ThumbLX > 12000, out var seen);
            results.Add(new("Remap round trip (RB -> A)", ok,
                $"read back buttons=0x{seen.Gamepad.Buttons:X4} RT={seen.Gamepad.RightTrigger} LX={seen.Gamepad.ThumbLX}"));

            engine.Process(default, 0.01, frame);
            pad.Submit(frame.Pad);
            bool released = WaitFor(pad.UserIndex, s => s.Gamepad.Buttons == 0 && s.Gamepad.RightTrigger == 0, out _);
            results.Add(new("Release round trip", released, released ? "all inputs released" : "virtual pad did not return to neutral"));
        }
        catch (Exception ex)
        {
            results.Add(new("Virtual controller", false, ex.Message));
        }
        finally
        {
            pad?.Dispose();
        }
        return results;
    }

    public static string Format(IReadOnlyList<DiagnosticResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("reController diagnostics");
        sb.AppendLine(new string('=', 60));
        foreach (var r in results)
        {
            string mark = r.Ok ? "PASS" : r.Critical ? "FAIL" : "WARN";
            sb.Append('[').Append(mark).Append("] ").Append(r.Check.PadRight(40)).Append(' ').AppendLine(r.Detail);
        }
        return sb.ToString();
    }

    private static bool WaitFor(int slot, Func<XInputNative.XInputState, bool> predicate, out XInputNative.XInputState last)
    {
        var sw = Stopwatch.StartNew();
        last = default;
        while (sw.ElapsedMilliseconds < 1500)
        {
            if (XInputNative.GetState(slot, out last) == XInputNative.ErrorSuccess && predicate(last)) return true;
            Thread.Sleep(10);
        }
        return false;
    }
}
