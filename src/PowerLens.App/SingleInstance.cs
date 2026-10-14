using System.Runtime.InteropServices;

namespace PowerLens.App;

/// <summary>
/// One PowerLens per user session. A second launch signals the first one to show its
/// window, then exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\PowerLens.SingleInstance";
    private const string ActivateEventName = @"Local\PowerLens.Activate";
    private const int ASFW_ANY = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activate;
    private RegisteredWaitHandle? _registration;

    public bool IsFirst { get; }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <param name="waitForPrevious">
    /// After an update the old version may still be shutting down; wait for it instead of
    /// treating this launch as a duplicate.
    /// </param>
    public SingleInstance(bool waitForPrevious = false)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (!created && waitForPrevious)
        {
            try { created = _mutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { created = true; } // Previous process exited without releasing.
        }
        IsFirst = created;
        if (IsFirst)
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
    }

    /// <summary>First instance: run <paramref name="onActivate"/> whenever another launch is attempted.</summary>
    public void ListenForActivation(Action onActivate)
    {
        if (_activate is null) return;
        _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Second instance: ask the running one to come to the front.</summary>
    public static void ActivateExisting()
    {
        try
        {
            // We are the foreground process (the user just launched us), so we may hand focus over.
            AllowSetForegroundWindow(ASFW_ANY);
            using var ev = EventWaitHandle.OpenExisting(ActivateEventName);
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The other instance is shutting down.
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activate?.Dispose();
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
