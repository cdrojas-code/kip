namespace StickyNotes.App.Services;

/// <summary>Garantiza una sola instancia. Una segunda instancia avisa a la primera
/// (mediante un EventWaitHandle con nombre) para que cree una nota nueva y termina.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\Kip.SingleInstance";
    private const string SignalName = @"Local\Kip.NewNoteRequested";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private RegisteredWaitHandle? _registration;

    public bool IsFirstInstance { get; }

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
    }

    /// <summary>Llamado por la segunda instancia: pide a la primera una nota nueva.</summary>
    public void SignalFirstInstance() => _signal.Set();

    /// <summary>Llamado por la primera instancia. El callback llega en un hilo del pool: el llamador debe pasar a UI.</summary>
    public void ListenForSignals(Action onSignal)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _signal, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _signal.Dispose();
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
