namespace ReController.App.Infrastructure;

/// <summary>Ensures one running instance; a second launch just brings the first window forward.</summary>
public sealed class SingleInstance : IDisposable
{
    private const string Id = "reController-7f3b9a2e-4c1d-4e8a-9b6f-2d5c8e1a0f47";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle? _wait;

    public SingleInstance(Action onActivateRequested)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{Id}", out bool created);
        IsFirst = created;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{Id}-activate");
        if (IsFirst)
            _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivateRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public bool IsFirst { get; }

    public void SignalFirstInstance() => _activate.Set();

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
