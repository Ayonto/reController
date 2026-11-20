using static ReController.Platform.Windows.Native.Win32;

namespace ReController.Platform.Windows.Services;

/// <summary>Reports the executable of the foreground window. Must be created on a thread with a message loop (UI thread).</summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly WinEventProc _proc; // keep delegate alive for the hook's lifetime
    private readonly IntPtr _hook;
    private string? _last;

    public ForegroundWatcher()
    {
        _proc = OnEvent;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _proc, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    /// <summary>Full path of the new foreground process executable.</summary>
    public event Action<string>? ForegroundChanged;

    public string? Current => _last;

    public static string? GetForegroundExecutable() => ExecutableOf(GetForegroundWindow());

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        string? exe = ExecutableOf(hwnd);
        if (exe is null || string.Equals(exe, _last, StringComparison.OrdinalIgnoreCase)) return;
        _last = exe;
        ForegroundChanged?.Invoke(exe);
    }

    private static string? ExecutableOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;
        IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = new char[1024];
            uint size = (uint)buf.Length;
            return QueryFullProcessImageName(h, 0, buf, ref size) ? new string(buf, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
    }
}
