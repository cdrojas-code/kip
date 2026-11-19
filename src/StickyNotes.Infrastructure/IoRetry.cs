namespace StickyNotes.Infrastructure;

/// <summary>
/// Reintenta operaciones de archivo ante bloqueos transitorios (antivirus, OneDrive, herramientas de backup).
/// Sólo reintenta errores de acceso/compartición; "archivo no encontrado" y errores de formato se propagan al instante.
/// </summary>
internal sealed class IoRetry
{
    public static readonly IoRetry Default = new(attempts: 5, delay: TimeSpan.FromMilliseconds(200));

    private readonly int _attempts;
    private readonly TimeSpan _delay;

    public IoRetry(int attempts, TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        _attempts = attempts;
        _delay = delay;
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _attempts && IsTransient(ex))
            {
                await Task.Delay(_delay, ct).ConfigureAwait(false);
            }
        }
    }

    public Task RunAsync(Func<Task> operation, CancellationToken ct) =>
        RunAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, ct);

    private static bool IsTransient(Exception ex) =>
        ex is UnauthorizedAccessException
        || (ex is IOException && ex is not FileNotFoundException and not DirectoryNotFoundException);
}
