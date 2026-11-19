using StickyNotes.App.Logging;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.App.Services;

/// <summary>Une la opción "Ocultar al compartir pantalla" con la plataforma:
/// <list type="bullet">
/// <item>protección de captura en cada ventana (notas y secundarias);</item>
/// <item>en modo BlackoutWithAutoHide (LTSC 2019), autoocultado mientras <see cref="IScreenShareDetector"/> detecta una sesión;</item>
/// <item>en TODOS los modos, el detector alimenta <see cref="IsContentPrivate"/>: los toasts de Windows no respetan
/// la afinidad de captura y saldrían en la pantalla compartida, así que se muestran sin contenido.</item>
/// </list></summary>
public sealed class ScreenSharePrivacyCoordinator : IDisposable
{
    private readonly INoteService _notes;
    private readonly IScreenPrivacyService _privacy;
    private readonly IScreenShareDetector _detector;
    private readonly NoteWindowManager _noteWindows;
    private readonly AppWindows _appWindows;
    private readonly FileLogger _log;
    private bool _detectorRunning;
    private bool _started;

    public ScreenSharePrivacyCoordinator(INoteService notes, IScreenPrivacyService privacy,
        IScreenShareDetector detector, NoteWindowManager noteWindows, AppWindows appWindows, FileLogger log)
    {
        _notes = notes;
        _privacy = privacy;
        _detector = detector;
        _noteWindows = noteWindows;
        _appWindows = appWindows;
        _log = log;
    }

    public bool IsEnabled => _notes.Settings.HideOnScreenShare;

    private bool UsesAutoHide => _privacy.Mode == ScreenPrivacyMode.BlackoutWithAutoHide;

    private bool IsSharingDetected => _detectorRunning && _detector.IsSharing;

    /// <summary>True si los avisos no deben mostrar contenido: pantalla compartida detectada, notas
    /// autoocultadas o modo presentación. Llamar en el hilo de UI.</summary>
    public bool IsContentPrivate =>
        IsSharingDetected || (_noteWindows.HideReasons & (HideReasons.ScreenShare | HideReasons.Presentation)) != 0;

    /// <summary>Llamar en el hilo de UI (el detector dispara sus eventos en el hilo que llamó a Start).</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _detector.SharingChanged += OnSharingChanged;
        // El modo puede degradarse a BlackoutWithAutoHide tras el primer Protect (si Windows rechaza
        // WDA_EXCLUDEFROMCAPTURE). Si al arrancar no había notas, se reevalúa al mostrarse cada una.
        _notes.NoteShown += OnNoteShown;
        UpdateDetector();
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_notes.Settings.HideOnScreenShare == enabled) return;
        _notes.Settings.HideOnScreenShare = enabled;
        _noteWindows.ApplyPrivacy(enabled);
        _appWindows.ApplyPrivacy(enabled);
        UpdateDetector();
        try
        {
            await _notes.SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            _log.Error("No se pudieron guardar los ajustes (privacidad)", ex);
        }
    }

    private void OnNoteShown(object? sender, NoteEventArgs e) => UpdateDetector();

    private void UpdateDetector()
    {
        if (IsEnabled && !_detectorRunning)
        {
            _detector.Start();
            _detectorRunning = true;
        }
        else if (!IsEnabled && _detectorRunning)
        {
            _detector.Stop();
            _detectorRunning = false;
        }
        ApplyAutoHide();
    }

    private void OnSharingChanged(object? sender, bool isSharing)
    {
        if (!IsEnabled) return;
        _log.Info(isSharing
            ? (UsesAutoHide ? "Pantalla compartida detectada: notas ocultas" : "Pantalla compartida detectada: avisos sin contenido")
            : "Fin de pantalla compartida");
        ApplyAutoHide();
    }

    /// <summary>Sólo en BlackoutWithAutoHide se ocultan las ventanas; con ExcludeFromCapture ya son invisibles para la captura.</summary>
    private void ApplyAutoHide() => _noteWindows.SetScreenShareHidden(IsEnabled && UsesAutoHide && IsSharingDetected);

    public void Dispose()
    {
        _notes.NoteShown -= OnNoteShown;
        _detector.SharingChanged -= OnSharingChanged;
        if (_detectorRunning) _detector.Stop();
        _detectorRunning = false;
    }
}
