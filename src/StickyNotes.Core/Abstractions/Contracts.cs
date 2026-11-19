using StickyNotes.Core.Models;

namespace StickyNotes.Core.Abstractions;

// ───────────── Tiempo ─────────────

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

// ───────────── Rutas ─────────────

public interface IAppPaths
{
    /// <summary>%LOCALAPPDATA%\Kip</summary>
    string DataDirectory { get; }
    string NotesFile { get; }
    string SettingsFile { get; }
    string BackupDirectory { get; }
    string LogDirectory { get; }
}

// ───────────── Persistencia (Infrastructure) ─────────────

public interface INoteRepository
{
    /// <summary>Carga todas las notas (incluida la papelera). Archivo inexistente = lista vacía. Archivo corrupto = intenta el backup más reciente.</summary>
    Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>Guarda todas las notas de forma atómica.</summary>
    Task SaveAllAsync(IReadOnlyCollection<Note> notes, CancellationToken ct = default);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

public interface IBackupService
{
    /// <summary>Crea el backup del día si aún no existe y elimina los que superan la retención.</summary>
    Task CreateDailyBackupIfNeededAsync(IReadOnlyCollection<Note> notes, int retentionDays, CancellationToken ct = default);

    Task ExportAsync(IReadOnlyCollection<Note> notes, string filePath, CancellationToken ct = default);

    /// <summary>Lee notas de un archivo exportado. No las guarda: el llamador decide cómo fusionarlas.</summary>
    Task<IReadOnlyList<Note>> ImportAsync(string filePath, CancellationToken ct = default);

    /// <summary>Rutas completas de los backups, más reciente primero.</summary>
    Task<IReadOnlyList<string>> ListBackupsAsync(CancellationToken ct = default);
}

// ───────────── Lógica de negocio (Core) ─────────────

public sealed class NoteEventArgs(Note note) : EventArgs
{
    public Note Note { get; } = note;
}

public interface INoteService
{
    /// <summary>Carga notas y ajustes, purga papelera vencida y crea el backup diario.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    AppSettings Settings { get; }

    /// <summary>Guarda los ajustes (inmediato).</summary>
    Task SaveSettingsAsync(CancellationToken ct = default);

    IReadOnlyList<Note> ActiveNotes { get; }
    IReadOnlyList<Note> TrashedNotes { get; }

    /// <summary>Nota que pasa a estar en pantalla (creada, restaurada o importada).</summary>
    event EventHandler<NoteEventArgs>? NoteShown;

    /// <summary>Nota que deja de estar en pantalla (a papelera o purgada).</summary>
    event EventHandler<NoteEventArgs>? NoteHidden;

    /// <summary>Crea una nota activa con los valores por defecto de <see cref="AppSettings"/> y dispara <see cref="NoteShown"/>.</summary>
    Note Create(double left, double top);

    /// <summary>Notifica que la nota cambió; actualiza UpdatedAt y programa guardado con debounce.</summary>
    void MarkChanged(Note note);

    void MoveToTrash(Guid id);
    void Restore(Guid id);
    void DeletePermanently(Guid id);
    void EmptyTrash();

    Note? Find(Guid id);

    /// <summary>Búsqueda insensible a mayúsculas y tildes en título, texto y checklist. Sólo notas activas.</summary>
    IReadOnlyList<Note> Search(string query);

    /// <summary>Fusiona notas importadas (Ids repetidos se reemplazan si son más recientes). Devuelve cuántas se añadieron/actualizaron.</summary>
    int Import(IReadOnlyList<Note> notes);

    /// <summary>Guarda inmediatamente (cancelando el debounce pendiente).</summary>
    Task FlushAsync(CancellationToken ct = default);
}

public interface IReminderMonitor
{
    /// <summary>Revisa recordatorios vencidos no notificados de notas activas, los notifica vía <see cref="INotifier"/> y los marca.</summary>
    void CheckDue();
}

// ───────────── Plataforma (Platform) ─────────────

public interface INotifier
{
    /// <summary>Notificación de Windows (toast). Al hacer clic se dispara <see cref="NotificationActivated"/> con el Id de la nota.</summary>
    void ShowReminder(Note note);

    /// <summary>Puede dispararse desde un hilo que no es de UI.</summary>
    event EventHandler<Guid>? NotificationActivated;
}

public enum ScreenPrivacyMode
{
    /// <summary>Win10 2004+ / Win11: la ventana no aparece en capturas.</summary>
    ExcludeFromCapture,

    /// <summary>LTSC 2019: el contenido aparece negro en capturas + autoocultado por detección.</summary>
    BlackoutWithAutoHide,
}

public interface IScreenPrivacyService
{
    ScreenPrivacyMode Mode { get; }

    /// <summary>Aplica la protección a una ventana (HWND). Idempotente.</summary>
    void Protect(nint hwnd);

    /// <summary>Quita la protección (cuando el usuario desactiva la opción).</summary>
    void Unprotect(nint hwnd);
}

public interface IScreenShareDetector : IDisposable
{
    /// <summary>True mientras se detecta una sesión de pantalla compartida.</summary>
    bool IsSharing { get; }

    /// <summary>Se dispara en el hilo que llamó a <see cref="Start"/> (UI) cuando cambia <see cref="IsSharing"/>.</summary>
    event EventHandler<bool>? SharingChanged;

    void Start();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716", Justification = "Sólo se consume desde C#; el nombre Start/Stop es el más claro.")]
    void Stop();
}

public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>Registra un atajo global ("Ctrl+Alt+N"). Devuelve false si el atajo está ocupado o es inválido. Callback en hilo de UI.</summary>
    bool Register(string gesture, Action callback);

    void UnregisterAll();
}

public interface IAutostartService
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
}

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height);

/// <summary>Monitores y visibilidad. Todas las coordenadas y tamaños están en PÍXELES FÍSICOS de la pantalla virtual.</summary>
public interface IScreenBoundsService
{
    /// <summary>Si la nota quedó fuera de todos los monitores (p. ej. se desconectó uno), devuelve una posición visible en el monitor principal; si no, la misma. Todo en píxeles.</summary>
    (double Left, double Top) EnsureVisible(double left, double top, double width, double height);

    /// <summary>Áreas de trabajo en píxeles; la primera es la del monitor principal.</summary>
    IReadOnlyList<ScreenRect> GetWorkAreas();
}
