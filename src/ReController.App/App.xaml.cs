using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ReController.App.Infrastructure;
using ReController.App.Services;
using ReController.App.ViewModels;
using ReController.App.Views;
using ReController.Platform.Windows.Runtime;
using ReController.Platform.Windows.Services;
using Serilog;

namespace ReController.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private ForegroundWatcher? _foreground;
    private MainWindow? _window;
    private int _dispatcherErrors;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        ConfigureLogging(elevatedHelper: args.Length > 0 && (args[0] == ElevatedBatch.Verb || args[0] == DriverSetup.Verb));

        // Elevated helper mode: install the bundled drivers silently and exit (no UI).
        if (args.Length >= 3 && args[0] == DriverSetup.Verb)
        {
            int code = DriverSetup.ExecuteElevated(args[1], args.Skip(2));
            Log.CloseAndFlush();
            Shutdown(code);
            return;
        }

        // Unattended / scripted setup: reController --install-drivers [vigembus] [hidhide]
        if (args.Length > 0 && args[0] == "--install-drivers")
        {
            bool all = args.Length == 1;
            bool vigem = all || args.Contains("vigembus", StringComparer.OrdinalIgnoreCase);
            bool hidhide = all || args.Contains("hidhide", StringComparer.OrdinalIgnoreCase);
            int exit;
            try
            {
                var result = DriverSetup.InstallAsync(vigem, hidhide).GetAwaiter().GetResult();
                foreach (var step in result.Steps) Console.Out.WriteLine($"[{(step.Ok ? "OK" : "FAIL")}] {step.Message} (exit {step.ExitCode})");
                if (result.RebootRequired) Console.Out.WriteLine("Restart Windows to finish.");
                exit = result.Ok ? (result.RebootRequired ? 3010 : 0) : 1;
            }
            catch (OperationCanceledException)
            {
                Console.Out.WriteLine("Administrator permission was declined.");
                exit = 1602;
            }
            Log.CloseAndFlush();
            Shutdown(exit);
            return;
        }

        // Elevated helper mode: run the requested HidHide commands and exit (no UI).
        if (args.Length == 3 && args[0] == ElevatedBatch.Verb)
        {
            int code = ElevatedBatch.Execute(args[1], args[2]);
            Log.CloseAndFlush();
            Shutdown(code);
            return;
        }

        // Diagnostics mode: exercise drivers + a virtual pad round trip, print a report and exit.
        if (args.Length > 0 && args[0] == "--selftest")
        {
            var results = Diagnostics.Run();
            string report = Diagnostics.Format(results);
            Console.Out.Write(report);
            if (args.Length > 1) File.WriteAllText(args[1], report);
            Log.Information("Self test:{NewLine}{Report}", Environment.NewLine, report);
            Log.CloseAndFlush();
            Shutdown(results.Any(r => r.Critical && !r.Ok) ? 1 : 0);
            return;
        }

        string? screenshot = ArgValue(args, "--screenshot");
        bool startHidden = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        if (screenshot is null)
        {
            _instance = new SingleInstance(() => Dispatcher.BeginInvoke(ShowMainWindow));
            if (!_instance.IsFirst)
            {
                _instance.SignalFirstInstance();
                Shutdown(0);
                return;
            }
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Fatal(ev.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", ev.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Error(ev.Exception, "Unobserved task exception"); ev.SetObserved(); };

        Log.Information("reController {Version} starting (portable: {Portable}, elevated: {Elevated})", AppInfo.Version, AppPaths.IsPortable, ElevatedBatch.IsElevated);

        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<RemapService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        var settings = _services.GetRequiredService<SettingsService>();
        ThemeManager.Apply(settings.Current.Accent);

        var remap = _services.GetRequiredService<RemapService>();
        remap.SetPollingRate(settings.Current.PollingRateHz);
        remap.SetSlotSwitchModifier(settings.Current.SlotSwitchModifier);
        remap.Start();

        var vm = _services.GetRequiredService<MainViewModel>();
        _window = _services.GetRequiredService<MainWindow>();

        if (screenshot is not null)
        {
            RunScreenshot(vm, screenshot, args);
            return;
        }

        _tray = new TrayIcon(ShowMainWindow, enabled => vm.RemapEnabled = enabled, ExitApplication);
        _tray.SetRemapState(vm.RemapEnabled);
        vm.RemapStateChanged += enabled => _tray?.SetRemapState(enabled);

        bool trayHintShown = false;
        _window.HiddenToTray += () =>
        {
            if (trayHintShown) return;
            trayHintShown = true;
            _tray?.Notify("reController is still running", "Remapping continues in the background. Right-click the tray icon to exit.");
        };

        _foreground = new ForegroundWatcher();
        _foreground.ForegroundChanged += exe => Dispatcher.BeginInvoke(() => vm.OnForegroundChanged(exe));

        if (startHidden || settings.Current.StartMinimized)
        {
            if (!settings.Current.FirstRunCompleted) ShowMainWindow();
            else _tray.Notify("reController is running", vm.RemapEnabled ? "Remapping is on." : "Remapping is off.");
        }
        else
        {
            ShowMainWindow();
        }
        if (_window.IsVisible) vm.ShowSetupIfNeeded();
        settings.Current.FirstRunCompleted = true;
        settings.Save();
    }

    private void ShowMainWindow()
    {
        if (_window is null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private void ExitApplication()
    {
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.GetService<MainViewModel>()?.Shutdown();
            _foreground?.Dispose();
            _tray?.Dispose();
            _services?.Dispose(); // disposes RemapService: releases keys, unplugs virtual pads
            _instance?.Dispose();
            Log.Information("reController exited");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during shutdown");
        }
        finally
        {
            Log.CloseAndFlush();
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        // Keep running for isolated faults; bail out if the UI is stuck in an error loop.
        if (++_dispatcherErrors > 20) return;
        e.Handled = true;
        _services?.GetService<MainViewModel>()?.ShowToast("Something went wrong: " + e.Exception.Message + " (details in the log)", isError: true);
    }

    private static void ConfigureLogging(bool elevatedHelper)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Pid", Environment.ProcessId)
            .WriteTo.File(
                Path.Combine(AppPaths.Logs, elevatedHelper ? "helper-.log" : "reController-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>
    /// Developer/docs aid: <c>--screenshot out.png [--config name] [--page 0-3] [--select A] [--overlay settings|about|setup]</c>
    /// renders the main window to a PNG and exits. Used for README images and visual regression checks.
    /// </summary>
    private void RunScreenshot(MainViewModel vm, string path, string[] args)
    {
        if (ArgValue(args, "--config") is { } cfgName && vm.SelectedGame?.Configs.FirstOrDefault(c => c.Name.Equals(cfgName, StringComparison.OrdinalIgnoreCase)) is { } cfg)
            vm.SelectedConfig = cfg;
        if (int.TryParse(ArgValue(args, "--page"), out int page)) vm.PageIndex = page;
        if (ArgValue(args, "--select") is { } sel && Enum.TryParse<Core.Input.Control>(sel, true, out var control)) vm.SelectControl(control);
        switch (ArgValue(args, "--overlay"))
        {
            case "settings": vm.OpenSettingsCommand.Execute(null); break;
            case "about": vm.OpenAboutCommand.Execute(null); break;
            case "setup": vm.OpenSetupCommand.Execute(null); break;
        }

        var w = _window!;
        w.ShowActivated = false;
        w.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var root = (FrameworkElement)w.Content;
                var dpi = VisualTreeHelper.GetDpi(w);
                var bmp = new RenderTargetBitmap(
                    (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bmp.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using var fs = File.Create(path);
                enc.Save(fs);
                Log.Information("Screenshot written to {Path}", path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Screenshot failed");
            }
            w.AllowClose = true;
            w.Close();
            Shutdown(0);
        };
        timer.Start();
    }
}
