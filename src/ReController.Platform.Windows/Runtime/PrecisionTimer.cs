using System.Diagnostics;
using static ReController.Platform.Windows.Native.Win32;

namespace ReController.Platform.Windows.Runtime;

/// <summary>Sub-millisecond periodic wait using a high-resolution waitable timer (Win10 1803+).</summary>
internal sealed class PrecisionTimer : IDisposable
{
    private readonly IntPtr _timer;
    private readonly bool _highRes;
    private long _nextTick;

    public PrecisionTimer()
    {
        _timer = CreateWaitableTimerEx(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        _highRes = _timer != IntPtr.Zero;
        if (!_highRes)
        {
            _ = timeBeginPeriod(1);
            _timer = CreateWaitableTimerEx(IntPtr.Zero, null, 0, TimerAllAccess);
        }
        _nextTick = Stopwatch.GetTimestamp();
    }

    /// <summary>Sleeps until the next period boundary. Falls back to catch-up if we overran.</summary>
    public void WaitNext(double periodSeconds)
    {
        long period = (long)(periodSeconds * Stopwatch.Frequency);
        _nextTick += period;
        long now = Stopwatch.GetTimestamp();
        long remaining = _nextTick - now;
        if (remaining <= 0)
        {
            // Overran (e.g. system hiccup): resynchronise instead of bursting.
            if (-remaining > period * 4) _nextTick = now;
            return;
        }

        long hundredNs = remaining * 10_000_000 / Stopwatch.Frequency;
        if (_timer != IntPtr.Zero && SetWaitableTimer(_timer, -hundredNs, 0, IntPtr.Zero, IntPtr.Zero, false))
            _ = WaitForSingleObject(_timer, Infinite);
        else
            Thread.Sleep(TimeSpan.FromTicks(hundredNs));
    }

    public void Dispose()
    {
        if (_timer != IntPtr.Zero) CloseHandle(_timer);
        if (!_highRes) _ = timeEndPeriod(1);
    }
}
