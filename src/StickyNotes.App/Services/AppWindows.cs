using System.Media;
using System.Windows;
using System.Windows.Interop;
using StickyNotes.App.ViewModels;
using StickyNotes.App.Views;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.App.Services;

/// <summary>Abre las ventanas secundarias (una sola instancia de búsqueda y de papelera) y les aplica la
/// misma privacidad que a las notas: protección de captura y ocultado mientras se comparte pantalla,
/// porque también muestran contenido de las notas.</summary>
public sealed class AppWindows
{
    private readonly INoteService _notes;
    private readonly IDialogService _dialogs;
    private readonly NoteWindowManager _noteWindows;
    private readonly IScreenPrivacyService _privacy;
    private readonly HashSet<Window> _hiddenByScreenShare = [];
    private SearchWindow? _search;
    private TrashWindow? _trash;

    public AppWindows(INoteService notes, IDialogService dialogs, NoteWindowManager noteWindows, IScreenPrivacyService privacy)
    {
        _notes = notes;
        _dialogs = dialogs;
        _noteWindows = noteWindows;
        _privacy = privacy;
        _noteWindows.HiddenStateChanged += (_, _) => SyncScreenShareVisibility();
    }

    /// <summary>True si los avisos no deben mostrar contenido (pantalla compartida o modo presentación).
    /// Lo asigna <see cref="AppController"/>.</summary>
    public Func<bool> IsContentPrivate { get; set; } = () => false;

    private bool IsScreenShareHidden => _noteWindows.HideReasons.HasFlag(HideReasons.ScreenShare);

    public void ShowSearch()
    {
        if (_search is null)
        {
            _search = new SearchWindow(new SearchViewModel(_notes, _noteWindows.FocusNote));
            Track(_search);
            _search.Closed += (_, _) => _search = null;
            _search.Show();
        }
        Bring(_search);
    }

    public void ShowTrash()
    {
        if (_trash is null)
        {
            _trash = new TrashWindow(new TrashViewModel(_notes, _dialogs));
            Track(_trash);
            _trash.Closed += (_, _) => _trash = null;
            _trash.Show();
        }
        Bring(_trash);
    }

    /// <summary>Aviso propio cuando el toast de Windows no está disponible. Se puede llamar desde cualquier hilo.</summary>
    public void ShowReminderFallback(Note note)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ShowReminderFallback(note));
            return;
        }

        bool isPrivate;
        try { isPrivate = IsContentPrivate(); }
        catch (Exception) { isPrivate = true; } // ante la duda, privado

        SystemSounds.Asterisk.Play();
        var popup = new ReminderFallbackPopup(note, isPrivate, () => _noteWindows.FocusNote(note.Id));
        Track(popup);
        // Mientras se comparte pantalla se difiere: se mostrará al terminar.
        if (IsScreenShareHidden) _hiddenByScreenShare.Add(popup);
        else popup.Show();
    }

    /// <summary>Aplica o quita la protección de captura en las ventanas secundarias abiertas.</summary>
    public void ApplyPrivacy(bool protect)
    {
        foreach (var window in SecondaryWindows()) ApplyPrivacy(window, protect);
    }

    public void CloseAll()
    {
        _hiddenByScreenShare.Clear();
        _search?.Close();
        _trash?.Close();
        foreach (var popup in Application.Current.Windows.OfType<ReminderFallbackPopup>().ToList()) popup.Close();
    }

    private void Track(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyPrivacy(window, _notes.Settings.HideOnScreenShare);
        window.Closed += (_, _) => _hiddenByScreenShare.Remove(window);
    }

    private void ApplyPrivacy(Window window, bool protect)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        if (protect) _privacy.Protect(hwnd);
        else _privacy.Unprotect(hwnd);
    }

    /// <summary>Oculta las ventanas secundarias mientras se detecta pantalla compartida y las restaura después.</summary>
    private void SyncScreenShareVisibility()
    {
        if (IsScreenShareHidden)
        {
            foreach (var window in SecondaryWindows().Where(w => w.IsVisible))
            {
                _hiddenByScreenShare.Add(window);
                window.Hide();
            }
            return;
        }

        foreach (var window in _hiddenByScreenShare.ToList()) window.Show();
        _hiddenByScreenShare.Clear();
    }

    private IEnumerable<Window> SecondaryWindows()
    {
        if (_search is not null) yield return _search;
        if (_trash is not null) yield return _trash;
        foreach (var popup in Application.Current.Windows.OfType<ReminderFallbackPopup>().ToList()) yield return popup;
    }

    private static void Bring(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
