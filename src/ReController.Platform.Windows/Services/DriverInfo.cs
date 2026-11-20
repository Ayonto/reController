using Microsoft.Win32;

namespace ReController.Platform.Windows.Services;

public sealed record DriverState(bool Installed, string? Version);

/// <summary>Detects the third-party kernel drivers reController relies on.</summary>
public static class DriverInfo
{
    public const string ViGEmBusDownloadUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";
    public const string HidHideDownloadUrl = "https://github.com/nefarius/HidHide/releases/latest";

    public static DriverState ViGEmBus() => Probe("ViGEmBus");

    public static DriverState HidHide() => Probe("HidHide");

    private static DriverState Probe(string service)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}");
            if (key is null) return new DriverState(false, null);
            string? image = key.GetValue("ImagePath") as string;
            string? version = null;
            if (!string.IsNullOrEmpty(image))
            {
                string path = Environment.ExpandEnvironmentVariables(image.Replace(@"\SystemRoot\", @"%SystemRoot%\", StringComparison.OrdinalIgnoreCase).TrimStart('\\', '?'));
                if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
                if (File.Exists(path)) version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
            }
            return new DriverState(true, version);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return new DriverState(false, null);
        }
    }
}
