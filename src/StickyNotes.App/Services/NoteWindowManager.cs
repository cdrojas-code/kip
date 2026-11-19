using System.Windows;
using System.Windows.Threading;
using StickyNotes.App.Interop;
using StickyNotes.App.Logging;
using StickyNotes.App.ViewModels;
using StickyNotes.App.Views;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.App.Services;

/// <summary>Dueño de las ventanas de nota: una por nota activa. Escucha NoteShown/NoteHidden,
/// gestiona el ocultado global (con motivos separados) y mantiene las notas por encima de todo.
/// Debe crearse y usarse en el hilo de UI (los métodos públicos que pueden llegar desde otros hilos lo indican).</summary>
public sealed class NoteWindowManager : IDisposable
{
    private static readonly TimeSpan TopmostInterval = TimeSpan.FromSeconds(3);

    // Distancias en DIPs; se convierten a píxeles con la escala del monitor principal (Note.Left/Top son píxeles).
    private const double CascadeStep = 24;
    private const double ScreenMargin = 40;
    private const double NearOffset = 30;

    private readonly INoteService _notes;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;
    private readonly IScreenPrivacyService _privacy;
    private readonly IScreenBoundsService _bounds;
    private readonly FileLogger _log;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, NoteWindow> _windows = new();
    private readonly DispatcherTimer _topmostTimer;

    private HideReasons _hideReasons;
    private Guid? _focusOnShow;
    private bool _started;

    public NoteWindowManager(INoteService notes, IDialogService dialogs, IClock clock,
        IScreenPrivacyService privacy, IScreenBoundsService bounds, FileLogger log)
    {
        _notes = notes;
        _dialogs = dialogs;
        _clock = clock;
        _privacy = privacy;
        _bounds = bounds;
        _log = log;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TopmostInterval };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
    }

    public bool IsHidden => _hideReasons != HideReasons.None;
    public bool IsPresentationMode => _hideReasons.HasFlag(HideReasons.Presentation);
    public HideReasons HideReasons => _hideReasons;

    public event EventHandler? HiddenStateChanged;

    /// <summary>Abre una ventana por cada nota activa y empieza a escuchar cambios.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _notes.NoteShown += OnNoteShown;
        _notes.NoteHidden += OnNoteHidden;
        foreach (var note in _notes.ActiveNotes.ToList()) OpenWindow(note);
        _topmostTimer.Start();
    }

    // ───────────── Crear / enfocar ─────────────

    /// <summary>Nota nueva pedida por el usuario (bandeja, doble clic, segunda instancia).</summary>
    public Note CreateNote()
    {
        RevealForUser();
        var scale = PrimaryScale();
        var (w, h) = PixelSize(_notes.Settings.DefaultSize, scale);
        var area = _bounds.GetWorkAreas().FirstOrDefault(new ScreenRect(0, 0, 1024, 768));
        var offset = _notes.ActiveNotes.Count % 8 * CascadeStep * scale;
        var left = area.Left + area.Width - w - ScreenMargin * scale - offset;
        var top = area.Top + ScreenMargin * scale + offset;
        return CreateAt(left, top, w, h);
    }

    /// <summary>Nota nueva junto a otra (botón "+" de la cabecera).</summary>
    public Note CreateNoteNear(Note source)
    {
        RevealForUser();
        var scale = PrimaryScale();
        var (w, h) = PixelSize(_notes.Settings.DefaultSize, scale);
        return CreateAt(source.Left + NearOffset * scale, source.Top + NearOffset * scale, w, h);
    }

    /// <summary>Escala del monitor principal (aproximación para convertir tamaños DIP → px).</summary>
    private static double PrimaryScale()
    {
        try { return Math.Clamp(StickyNotes.Platform.ScreenBoundsService.GetPrimaryScale(), 1.0, 5.0); }
        catch (Exception) { return 1.0; }
    }

    private static (double Width, double Height) PixelSize(NoteSize size, double scale)
    {
        var (w, h) = size.ToDimensions();
        return (Math.Round(w * scale), Math.Round(h * scale));
    }

    private Note CreateAt(double left, double top, double width, double height)
    {
        var (l, t) = _bounds.EnsureVisible(left, top, width, height);
        var note = _notes.Create(l, t);
        _focusOnShow = note.Id;
        if (_windows.TryGetValue(note.Id, out var existing)) FocusWindow(existing);
        return note;
    }

    /// <summary>Muestra y resalta una nota. Se puede llamar desde cualquier hilo.</summary>
    public void FocusNote(Guid id)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => FocusNote(id));
            return;
        }
        if (!_windows.TryGetValue(id, out var window)) return;
        RevealForUser();
        FocusWindow(window);
    }

    private void FocusWindow(NoteWindow window)
    {
        // Durante una pantalla compartida detectada no se revela nada (privacidad del cliente).
        if (IsHidden) return;
        window.FocusAndFlash();
    }

    // ───────────── Ocultar / mostrar ─────────────

    /// <summary>Atajo Ctrl+Alt+N: si hay algo oculto, el usuario quiere ver sus notas (anula todos los motivos).</summary>
    public void ToggleAll() => SetReasons(IsHidden ? HideReasons.None : HideReasons.Manual);

    public void SetPresentationMode(bool enabled) => SetReason(HideReasons.Presentation, enabled);

    public void SetScreenShareHidden(bool hidden) => SetReason(HideReasons.ScreenShare, hidden);

    /// <summary>Una acción explícita del usuario anula el ocultado manual y el de presentación, nunca el automático.</summary>
    private void RevealForUser() =>
        SetReasons(_hideReasons & ~(HideReasons.Manual | HideReasons.Presentation));

    private void SetReason(HideReasons reason, bool on) =>
        SetReasons(on ? _hideReasons | reason : _hideReasons & ~reason);

    private void SetReasons(HideReasons reasons)
    {
        if (reasons == _hideReasons) return;
        var wasHidden = IsHidden;
        _hideReasons = reasons;
        if (wasHidden != IsHidden)
        {
            foreach (var window in _windows.Values)
            {
                if (IsHidden) { window.CommitPendingEdits(); window.Hide(); }
                else window.Show();
            }
        }
        HiddenStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ───────────── Mantenimiento ─────────────

    /// <summary>Aplica o quita la protección de captura en todas las notas.</summary>
    public void ApplyPrivacy(bool protect)
    {
        foreach (var window in _windows.Values) window.ApplyPrivacy(protect);
    }

    /// <summary>Recoloca las notas que quedaron fuera de pantalla (p. ej. al desconectar un monitor).</summary>
    public void EnsureAllVisible()
    {
        var scale = PrimaryScale();
        foreach (var window in _windows.Values)
        {
            var note = window.ViewModel.Note;
            var (w, h) = PixelSize(note.Size, scale);
            var (l, t) = _bounds.EnsureVisible(note.Left, note.Top, w, h);
            if (Math.Abs(l - note.Left) < 0.5 && Math.Abs(t - note.Top) < 0.5) continue;
            window.MoveToPixels(l, t);
        }
    }

    public void RefreshReminderStates()
    {
        foreach (var window in _windows.Values) window.ViewModel.RefreshReminderState();
    }

    /// <summary>Vuelca a las notas lo pendiente de debounce en las ventanas (antes de FlushAsync).</summary>
    public void CommitAllPendingEdits()
    {
        foreach (var window in _windows.Values) window.CommitPendingEdits();
    }

    private void ReassertTopmost()
    {
        // Si el usuario está en una ventana nuestra (búsqueda, papelera, diálogo) no se le tapa.
        // Tampoco durante presentaciones, vídeo/apps a pantalla completa o con un menú abierto.
        if (IsHidden || NativeMethods.IsForegroundOwnedByThisProcess() || NativeMethods.ShouldYieldTopmost()) return;
        foreach (var window in _windows.Values) window.ReassertTopmost();
    }

    // ───────────── Eventos de INoteService ─────────────

    private void OnNoteShown(object? sender, NoteEventArgs e) =>
        // Diferido: quien crea la nota puede terminar de rellenarla (p. ej. la nota de bienvenida)
        // antes de que la ventana lea su contenido. Además garantiza el hilo de UI.
        _dispatcher.BeginInvoke(() =>
        {
            if (e.Note.IsInTrash) return;
            var window = OpenWindow(e.Note);
            if (window is not null && _focusOnShow == e.Note.Id)
            {
                _focusOnShow = null;
                FocusWindow(window);
            }
        });

    private void OnNoteHidden(object? sender, NoteEventArgs e) =>
        _dispatcher.BeginInvoke(() => CloseWindow(e.Note.Id));

    /// <summary>Abre la ventana de una nota. Nunca lanza: una nota dañada (p. ej. importada) se reintenta
    /// como texto plano y, si aun así falla, se registra y se omite sin impedir el arranque.</summary>
    private NoteWindow? OpenWindow(Note note)
    {
        if (_windows.TryGetValue(note.Id, out var existing)) return existing;

        try
        {
            var (w, h) = PixelSize(note.Size, PrimaryScale());
            var (l, t) = _bounds.EnsureVisible(note.Left, note.Top, w, h);
            if (Math.Abs(l - note.Left) > 0.5 || Math.Abs(t - note.Top) > 0.5)
            {
                note.Left = l;
                note.Top = t;
                _notes.MarkChanged(note);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"No se pudo comprobar la posición de la nota {note.Id}", ex);
        }

        NoteWindow? window;
        try
        {
            window = CreateWindow(note);
        }
        catch (Exception ex)
        {
            _log.Error($"No se pudo abrir la nota {note.Id}; se reintenta como texto plano", ex);
            // Se reintenta sin formato, pero sin perder el original: se restaura tras crear la ventana para que
            // un guardado posterior no borre el formato si el usuario no edita la nota.
            var original = note.RichContent;
            try
            {
                note.RichContent = string.Empty;
                window = CreateWindow(note);
            }
            catch (Exception retryEx)
            {
                _log.Error($"La nota {note.Id} no se puede mostrar y se omite", retryEx);
                return null;
            }
            finally
            {
                note.RichContent = original;
            }
        }

        _windows[note.Id] = window;
        if (!IsHidden) window.Show();
        return window;
    }

    private NoteWindow CreateWindow(Note note)
    {
        var vm = new NoteViewModel(note, _notes, _dialogs, _clock, source => CreateNoteNear(source));
        return new NoteWindow(vm, _privacy, () => _notes.Settings.HideOnScreenShare);
    }

    private void CloseWindow(Guid id)
    {
        if (!_windows.Remove(id, out var window)) return;
        window.AllowClose = true;
        window.Close();
    }

    public void CloseAll()
    {
        foreach (var id in _windows.Keys.ToList()) CloseWindow(id);
    }

    public void Dispose()
    {
        _topmostTimer.Stop();
        if (_started)
        {
            _notes.NoteShown -= OnNoteShown;
            _notes.NoteHidden -= OnNoteHidden;
        }
        CloseAll();
    }
}
