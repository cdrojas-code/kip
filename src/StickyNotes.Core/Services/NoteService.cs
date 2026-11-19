using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.Core.Services;

/// <summary>
/// Lógica de negocio de las notas: ciclo de vida (crear, papelera, restaurar, borrar),
/// búsqueda, importación y guardado con debounce.
/// Las mutaciones se esperan en el hilo de UI; el guardado se ejecuta en el thread pool
/// sobre una instantánea tomada bajo lock, y nunca hay dos escrituras solapadas.
/// </summary>
public sealed class NoteService : INoteService, IDisposable
{
    /// <summary>Espera por defecto entre el último cambio y el guardado.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(700);

    private readonly INoteRepository _repo;
    private readonly ISettingsStore _settingsStore;
    private readonly IBackupService _backups;
    private readonly IClock _clock;
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Debouncer _debouncer;
    private readonly List<Note> _notes = [];

    public NoteService(INoteRepository repo, ISettingsStore settingsStore, IBackupService backups, IClock clock)
        : this(repo, settingsStore, backups, clock, DefaultDebounce)
    {
    }

    internal NoteService(INoteRepository repo, ISettingsStore settingsStore, IBackupService backups, IClock clock, TimeSpan debounce,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(clock);

        _repo = repo;
        _settingsStore = settingsStore;
        _backups = backups;
        _clock = clock;
        _debouncer = new Debouncer(debounce, () => SaveNowAsync(CancellationToken.None), OnSaveFailed, OnSaveRecovered, retryDelays);
    }

    /// <summary>
    /// True sólo tras cargar correctamente. Mientras sea false no se escribe nada en disco:
    /// un arranque fallido nunca debe sobrescribir notes.json con una lista vacía.
    /// </summary>
    private volatile bool _initialized;

    /// <summary>Instantánea previa a una importación, pendiente de escribirse como backup de seguridad.</summary>
    private List<Note>? _pendingSafetySnapshot;

    public AppSettings Settings { get; private set; } = new();

    public IReadOnlyList<Note> ActiveNotes => Snapshot(n => !n.IsInTrash);

    public IReadOnlyList<Note> TrashedNotes => Snapshot(n => n.IsInTrash);

    public event EventHandler<NoteEventArgs>? NoteShown;

    public event EventHandler<NoteEventArgs>? NoteHidden;

    /// <summary>Error durante un guardado en segundo plano. Puede dispararse desde un hilo que no es de UI.</summary>
    public event EventHandler<Exception>? SaveFailed;

    /// <summary>Un guardado tuvo éxito tras una racha de fallos. Puede dispararse desde un hilo que no es de UI.</summary>
    public event EventHandler? SaveRecovered;

    /// <summary>True cuando las notas se cargaron correctamente y se permite guardar.</summary>
    public bool IsInitialized => _initialized;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Settings = (await _settingsStore.LoadAsync(ct).ConfigureAwait(false)).Normalize();
        var loaded = await _repo.LoadAllAsync(ct).ConfigureAwait(false);

        lock (_sync)
        {
            _notes.Clear();
            _notes.AddRange(loaded);
        }

        _initialized = true;

        // El backup diario se hace ANTES de purgar la papelera, para que contenga el estado tal como se cargó.
        try
        {
            await _backups.CreateDailyBackupIfNeededAsync(TakeSnapshot(), Settings.BackupRetentionDays, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un backup fallido no debe impedir usar la app; se informa como fallo de guardado.
            OnSaveFailed(ex);
        }

        var cutoff = _clock.Now.AddDays(-Settings.TrashRetentionDays);
        int purged;
        lock (_sync)
        {
            purged = _notes.RemoveAll(n => n.DeletedAt is { } deletedAt && deletedAt <= cutoff);
        }

        if (purged > 0)
        {
            await SaveNowAsync(ct).ConfigureAwait(false);
        }
    }

    public Task SaveSettingsAsync(CancellationToken ct = default) =>
        _initialized ? _settingsStore.SaveAsync(Settings, ct) : Task.CompletedTask;

    public Note Create(double left, double top)
    {
        var now = _clock.Now;
        var note = new Note
        {
            Color = Settings.DefaultColor,
            Size = Settings.DefaultSize,
            FontSize = Settings.DefaultFontSize,
            Left = left,
            Top = top,
            CreatedAt = now,
            UpdatedAt = now,
        };

        lock (_sync)
        {
            _notes.Add(note);
        }

        NoteShown?.Invoke(this, new NoteEventArgs(note));
        _debouncer.Schedule();
        return note;
    }

    public void MarkChanged(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        note.UpdatedAt = _clock.Now;
        _debouncer.Schedule();
    }

    public void MoveToTrash(Guid id)
    {
        var note = Find(id);
        if (note is null || note.IsInTrash)
        {
            return;
        }

        var now = _clock.Now;
        note.DeletedAt = now;
        note.UpdatedAt = now;
        NoteHidden?.Invoke(this, new NoteEventArgs(note));
        _debouncer.Schedule();
    }

    public void Restore(Guid id)
    {
        var note = Find(id);
        if (note is null || !note.IsInTrash)
        {
            return;
        }

        note.DeletedAt = null;
        note.UpdatedAt = _clock.Now;
        NoteShown?.Invoke(this, new NoteEventArgs(note));
        _debouncer.Schedule();
    }

    public void DeletePermanently(Guid id)
    {
        Note? removed;
        lock (_sync)
        {
            removed = _notes.Find(n => n.Id == id);
            if (removed is not null)
            {
                _notes.Remove(removed);
            }
        }

        if (removed is null)
        {
            return;
        }

        if (!removed.IsInTrash)
        {
            NoteHidden?.Invoke(this, new NoteEventArgs(removed));
        }

        _debouncer.Schedule();
    }

    public void EmptyTrash()
    {
        int removed;
        lock (_sync)
        {
            removed = _notes.RemoveAll(n => n.IsInTrash);
        }

        if (removed > 0)
        {
            _debouncer.Schedule();
        }
    }

    public Note? Find(Guid id)
    {
        lock (_sync)
        {
            return _notes.Find(n => n.Id == id);
        }
    }

    public IReadOnlyList<Note> Search(string query) => NoteSearch.Search(ActiveNotes, query);

    public int Import(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        // Eventos a disparar fuera del lock: (nota, true = mostrada / false = ocultada).
        var events = new List<(Note Note, bool Shown)>();
        var count = 0;

        lock (_sync)
        {
            // Instantánea de seguridad del estado previo: se escribe en el próximo guardado.
            if (_notes.Count > 0 && _backups is ISafetyBackup && _pendingSafetySnapshot is null)
            {
                _pendingSafetySnapshot = _notes.ConvertAll(NoteCopy.Clone);
            }

            foreach (var raw in notes)
            {
                if (raw is null || raw.Id == Guid.Empty)
                {
                    continue;
                }

                var incoming = NoteSanitizer.Sanitize(raw);
                var index = _notes.FindIndex(n => n.Id == incoming.Id);
                if (index < 0)
                {
                    _notes.Add(incoming);
                }
                else
                {
                    var existing = _notes[index];
                    if (incoming.UpdatedAt <= existing.UpdatedAt)
                    {
                        continue;
                    }

                    _notes[index] = incoming;
                    if (!existing.IsInTrash)
                    {
                        events.Add((existing, false));
                    }
                }

                if (!incoming.IsInTrash)
                {
                    events.Add((incoming, true));
                }

                count++;
            }
        }

        foreach (var (note, shown) in events)
        {
            (shown ? NoteShown : NoteHidden)?.Invoke(this, new NoteEventArgs(note));
        }

        if (count > 0)
        {
            _debouncer.Schedule();
        }

        return count;
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        _debouncer.Cancel();
        await SaveNowAsync(ct).ConfigureAwait(false);
        _debouncer.MarkSucceeded();
    }

    /// <summary>Cancela el guardado pendiente. Llamar a <see cref="FlushAsync"/> antes para no perder cambios.</summary>
    public void Dispose()
    {
        _debouncer.Dispose();
        _writeGate.Dispose();
    }

    private async Task SaveNowAsync(CancellationToken ct)
    {
        if (!_initialized)
        {
            return;
        }

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WritePendingSafetySnapshotAsync(ct).ConfigureAwait(false);

            // La instantánea se toma dentro de la sección exclusiva: la última escritura siempre es la más reciente.
            await _repo.SaveAllAsync(TakeSnapshot(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task WritePendingSafetySnapshotAsync(CancellationToken ct)
    {
        List<Note>? pending;
        lock (_sync)
        {
            pending = _pendingSafetySnapshot;
            _pendingSafetySnapshot = null;
        }

        if (pending is null || _backups is not ISafetyBackup safety)
        {
            return;
        }

        try
        {
            await safety.CreateSafetySnapshotAsync(pending, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No bloquear el guardado de las notas por un backup de seguridad fallido: se informa.
            OnSaveFailed(ex);
        }
    }

    private List<Note> TakeSnapshot()
    {
        lock (_sync)
        {
            return _notes.ConvertAll(NoteCopy.Clone);
        }
    }

    private List<Note> Snapshot(Predicate<Note> filter)
    {
        lock (_sync)
        {
            return _notes.FindAll(filter);
        }
    }

    private void OnSaveFailed(Exception ex) => SaveFailed?.Invoke(this, ex);

    private void OnSaveRecovered() => SaveRecovered?.Invoke(this, EventArgs.Empty);
}
