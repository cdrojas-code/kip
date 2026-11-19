using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using StickyNotes.App.Logging;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;
using StickyNotes.Core.Services;
using StickyNotes.Platform;

namespace StickyNotes.App.Services;

/// <summary>Orquesta el ciclo de vida: arranque (carga, autoarranque, ventanas, bandeja, atajo,
/// recordatorios) y salida ordenada (guardar, liberar bandeja/atajos/detector).</summary>
public sealed class AppController
{
    private static readonly TimeSpan SaveFailedNotifyThrottle = TimeSpan.FromMinutes(5);

    private readonly NoteService _noteService;
    private readonly NoteWindowManager _noteWindows;
    private readonly TrayIconService _tray;
    private readonly AppWindows _windows;
    private readonly ReminderScheduler _reminders;
    private readonly ScreenSharePrivacyCoordinator _privacy;
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly IAutostartService _autostart;
    private readonly ToastNotifier _notifier;
    private readonly FileLogger _log;

    private static readonly TimeSpan CommitOnUiTimeout = TimeSpan.FromSeconds(1);

    private DateTime _lastSaveFailedNotice = DateTime.MinValue;

    /// <summary>True sólo cuando las notas se cargaron bien. Si la carga falló, NUNCA se guarda:
    /// se escribiría una lista vacía sobre notes.json.</summary>
    private bool _loaded;
    private bool _started;
    private bool _stopped;

    public AppController(NoteService noteService, NoteWindowManager noteWindows, TrayIconService tray,
        AppWindows windows, ReminderScheduler reminders, ScreenSharePrivacyCoordinator privacy,
        IGlobalHotkeyService hotkeys, IAutostartService autostart, ToastNotifier notifier, FileLogger log)
    {
        _noteService = noteService;
        _noteWindows = noteWindows;
        _tray = tray;
        _windows = windows;
        _reminders = reminders;
        _privacy = privacy;
        _hotkeys = hotkeys;
        _autostart = autostart;
        _notifier = notifier;
        _log = log;
    }

    /// <summary>"Salir" en la bandeja: el host (App) cierra la aplicación tras <see cref="StopAsync"/>.</summary>
    public event EventHandler? ExitRequested;

    public async Task StartAsync(bool startedWithWindows)
    {
        _noteService.SaveFailed += OnSaveFailed;
        await _noteService.InitializeAsync();
        _loaded = true;

        EnsureAutostart();

        // Los avisos (toast y ventana propia) no muestran contenido mientras se comparte pantalla o en presentación.
        _notifier.IsPrivate = () => _privacy.IsContentPrivate;
        _windows.IsContentPrivate = () => _privacy.IsContentPrivate;

        _noteWindows.Start();
        _privacy.Start();

        _tray.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _tray.Show();

        RegisterHotkey();

        _notifier.NotificationActivated += OnNotificationActivated;
        _notifier.FallbackRequested += OnFallbackRequested;
        _reminders.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (_noteService.ActiveNotes.Count == 0 && !startedWithWindows) CreateWelcomeNote();

        _started = true;
        _log.Info($"Kip iniciado ({_noteService.ActiveNotes.Count} notas activas, autoarranque={startedWithWindows})");
    }

    /// <summary>Nota nueva pedida desde fuera (segunda instancia). Se puede llamar desde cualquier hilo.</summary>
    public void RequestNewNote()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (_started) _noteWindows.CreateNote();
        });
    }

    /// <summary>Guarda todo y libera recursos nativos. Idempotente.</summary>
    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _notifier.NotificationActivated -= OnNotificationActivated;
        _notifier.FallbackRequested -= OnFallbackRequested;
        _reminders.Dispose();

        // Orden: primero se vuelcan las ediciones y se cierran TODAS las ventanas (así nadie puede seguir
        // escribiendo y OnClosing confirma lo pendiente); el guardado va al final.
        SafeDispose("ediciones pendientes", _noteWindows.CommitAllPendingEdits);
        SafeDispose("ventanas", _windows.CloseAll);
        SafeDispose("notas", _noteWindows.Dispose);

        if (_loaded)
        {
            try
            {
                await _noteService.FlushAsync();
            }
            catch (Exception ex)
            {
                _log.Error("Error al guardar al salir", ex);
            }
        }
        else
        {
            _log.Warn("Salida sin guardar: las notas no llegaron a cargarse (se conserva notes.json tal cual)");
        }

        SafeDispose("bandeja", _tray.Dispose);
        SafeDispose("atajos", _hotkeys.Dispose);
        SafeDispose("detector", _privacy.Dispose);
        _noteService.SaveFailed -= OnSaveFailed;
        _log.Info("Kip cerrado");
    }

    /// <summary>Guardado síncrono de emergencia (cierre de sesión, excepción no controlada).
    /// Se ejecuta en el pool para no bloquear el hilo de UI si FlushAsync captura el contexto.</summary>
    public void FlushNow(TimeSpan timeout)
    {
        // Si la carga no terminó, guardar escribiría una lista vacía sobre las notas del usuario.
        if (!_loaded) return;
        try
        {
            CommitPendingEditsFromAnyThread();
            Task.Run(() => _noteService.FlushAsync()).Wait(timeout);
        }
        catch (Exception ex)
        {
            _log.Error("Error en el guardado de emergencia", ex);
        }
    }

    /// <summary>Vuelca lo pendiente de las ventanas. Desde otro hilo (excepción de dominio) se pasa al hilo
    /// de UI con un tiempo máximo, por si éste está bloqueado.</summary>
    private void CommitPendingEditsFromAnyThread()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownFinished) return;
        try
        {
            if (dispatcher.CheckAccess()) _noteWindows.CommitAllPendingEdits();
            else dispatcher.Invoke(_noteWindows.CommitAllPendingEdits, DispatcherPriority.Send, CancellationToken.None, CommitOnUiTimeout);
        }
        catch (Exception ex)
        {
            _log.Warn("No se pudieron volcar las ediciones pendientes", ex);
        }
    }

    // ───────────── Arranque ─────────────

    private void EnsureAutostart()
    {
        try
        {
            if (_noteService.Settings.StartWithWindows && !_autostart.IsEnabled) _autostart.Enable();
        }
        catch (Exception ex)
        {
            _log.Warn("No se pudo activar el inicio con Windows", ex);
        }
    }

    private void RegisterHotkey()
    {
        var gesture = _noteService.Settings.ToggleHotkey;
        bool ok;
        try
        {
            ok = _hotkeys.Register(gesture, _noteWindows.ToggleAll);
        }
        catch (Exception ex)
        {
            _log.Warn($"Error al registrar el atajo {gesture}", ex);
            ok = false;
        }
        if (!ok)
        {
            _log.Warn($"Atajo global no disponible: {gesture}");
            _tray.ShowWarning("Atajo no disponible",
                $"El atajo {gesture} lo está usando otra aplicación. Use el menú de la bandeja para ocultar/mostrar las notas.");
        }
    }

    private void CreateWelcomeNote()
    {
        var note = _noteWindows.CreateNote();
        note.Title = "Bienvenido a Kip";
        note.PlainText =
            "Escriba aquí como en un post-it.\n" +
            "• Arrastre la nota desde la franja superior.\n" +
            "• + crea otra nota; el candado fija la posición.\n" +
            "• El reloj programa un recordatorio.\n" +
            $"• {_noteService.Settings.ToggleHotkey} oculta o muestra todas las notas.\n" +
            "• Más opciones en el icono de la bandeja (junto al reloj de Windows).";
        _noteService.MarkChanged(note);
    }

    // ───────────── Eventos ─────────────

    private void OnNotificationActivated(object? sender, Guid noteId) => _noteWindows.FocusNote(noteId);

    private void OnFallbackRequested(object? sender, Note note) => _windows.ShowReminderFallback(note);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        // Se espera un momento: justo tras el cambio las áreas de trabajo aún no son definitivas.
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            _noteWindows.EnsureAllVisible();
        });

    private void OnSaveFailed(object? sender, Exception ex)
    {
        _log.Error("Error al guardar las notas", ex);
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (DateTime.Now - _lastSaveFailedNotice < SaveFailedNotifyThrottle) return;
            _lastSaveFailedNotice = DateTime.Now;
            _tray.ShowWarning("No se pudieron guardar las notas",
                "Kip lo reintentará automáticamente. Si el problema continúa, exporte sus notas desde el menú de la bandeja.");
        });
    }

    private void SafeDispose(string name, Action dispose)
    {
        try { dispose(); }
        catch (Exception ex) { _log.Warn($"Error al liberar {name}", ex); }
    }
}
