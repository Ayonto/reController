using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ReController.App.Infrastructure;

internal static partial class NativeWindow
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Rounded corners + dark frame on Windows 11; harmless no-op on Windows 10.</summary>
    public static void ApplyModernFrame(Window w)
    {
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        int round = DwmwcpRound;
        int dark = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }
}
