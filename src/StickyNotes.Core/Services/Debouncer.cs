namespace StickyNotes.Core.Services;

/// <summary>
/// Agrupa varias solicitudes seguidas en una sola ejecución tras un periodo de calma.
/// La acción se ejecuta en el thread pool. Si falla, se reintenta con espera creciente
/// (<see cref="DefaultRetryDelays"/>) hasta que tenga éxito o llegue una nueva solicitud / cancelación.
/// <c>onError</c> se invoca sólo en el primer fallo de cada racha; <c>onRecovered</c> cuando la racha termina.
/// </summary>
internal sealed class Debouncer : IDisposable
{
    /// <summary>Esperas entre reintentos; la última se repite indefinidamente.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    ];

    private readonly TimeSpan _delay;
    private readonly Func<Task> _action;
    private readonly Action<Exception> _onError;
    private readonly Action? _onRecovered;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly Lock _sync = new();
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private bool _inFailureStreak;

    public Debouncer(TimeSpan delay, Func<Task> action, Action<Exception> onError, Action? onRecovered = null,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _delay = delay;
        _action = action;
        _onError = onError;
        _onRecovered = onRecovered;
        _retryDelays = retryDelays is { Count: > 0 } ? retryDelays : DefaultRetryDelays;
    }

    /// <summary>Programa la acción; reinicia la espera si ya había una pendiente (o un reintento).</summary>
    public void Schedule()
    {
        CancellationToken token;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            CancelPendingCore();
            _pending = new CancellationTokenSource();
            token = _pending.Token;
        }

        _ = Task.Run(() => RunAsync(token), CancellationToken.None);
    }

    /// <summary>Cancela la ejecución pendiente (o el reintento), si lo hay.</summary>
    public void Cancel()
    {
        lock (_sync)
        {
            CancelPendingCore();
        }
    }

    /// <summary>Indica que la acción tuvo éxito por otra vía (p. ej. un guardado inmediato): cierra la racha de fallos.</summary>
    public void MarkSucceeded() => EndFailureStreak();

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            CancelPendingCore();
        }
    }

    private void CancelPendingCore()
    {
        if (_pending is null)
        {
            return;
        }

        _pending.Cancel();
        _pending.Dispose();
        _pending = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        if (!await DelayAsync(_delay, token).ConfigureAwait(false))
        {
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _action().ConfigureAwait(false);
                EndFailureStreak();
                return;
            }
#pragma warning disable CA1031 // Un fallo en segundo plano nunca debe tumbar el proceso: se reporta y se reintenta.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                bool first;
                lock (_sync)
                {
                    first = !_inFailureStreak;
                    _inFailureStreak = true;
                }

                if (first)
                {
                    _onError(ex);
                }
            }

            var wait = _retryDelays[Math.Min(attempt, _retryDelays.Count - 1)];
            if (!await DelayAsync(wait, token).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private void EndFailureStreak()
    {
        bool recovered;
        lock (_sync)
        {
            recovered = _inFailureStreak;
            _inFailureStreak = false;
        }

        if (recovered)
        {
            _onRecovered?.Invoke();
        }
    }

    /// <returns>false si se canceló.</returns>
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
