using System.IO;

namespace ReController.App.Infrastructure;

/// <summary>
/// Data locations. If a <c>portable.txt</c> file sits next to the executable, everything is stored
/// in a <c>data</c> folder beside it instead of the user profile (USB-stick friendly).
/// </summary>
public static class AppPaths
{
    static AppPaths()
    {
        string exeDir = AppContext.BaseDirectory;
        IsPortable = File.Exists(Path.Combine(exeDir, "portable.txt"));
        Root = IsPortable
            ? Path.Combine(exeDir, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "reController");
        Logs = IsPortable
            ? Path.Combine(Root, "logs")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "reController", "logs");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
    }

    public static bool IsPortable { get; }
    public static string Root { get; }
    public static string Logs { get; }
    public static string Profiles => Path.Combine(Root, "profiles");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
}
