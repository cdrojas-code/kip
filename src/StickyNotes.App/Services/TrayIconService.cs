using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using StickyNotes.App.Logging;
using StickyNotes.Core.Abstractions;
using Application = System.Windows.Application;

namespace StickyNotes.App.Services;

/// <summary>Icono de la bandeja del sistema con el menú principal en español.
/// Hay que llamar a Dispose al salir: si no, Windows deja un icono "fantasma".</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly INoteService _notes;
    private readonly NoteWindowManager _noteWindows;
    private readonly AppWindows _windows;
    private readonly ImportExportService _importExport;
    private readonly IAutostartService _autostart;
    private readonly ScreenSharePrivacyCoordinator _privacy;
    private readonly IAppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly FileLogger _log;

    private NotifyIcon? _icon;
    private Icon? _iconImage;
    private Font? _boldFont;
    private ToolStripMenuItem? _toggleItem;
    private ToolStripMenuItem? _presentationItem;
    private ToolStripMenuItem? _autostartItem;
    private ToolStripMenuItem? _privacyItem;

    public TrayIconService(INoteService notes, NoteWindowManager noteWindows, AppWindows windows,
        ImportExportService importExport, IAutostartService autostart, ScreenSharePrivacyCoordinator privacy,
        IAppPaths paths, IDialogService dialogs, FileLogger log)
    {
        _notes = notes;
        _noteWindows = noteWindows;
        _windows = windows;
        _importExport = importExport;
        _autostart = autostart;
        _privacy = privacy;
        _paths = paths;
        _dialogs = dialogs;
        _log = log;
    }

    /// <summary>El usuario eligió "Salir".</summary>
    public event EventHandler? ExitRequested;

    public void Show()
    {
        if (_icon is not null) return;

        _iconImage = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _iconImage,
            Text = "Kip",
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _noteWindows.CreateNote();
        };
    }

    /// <summary>Globo informativo (p. ej. atajo ocupado o error al guardar).</summary>
    public void ShowWarning(string title, string text)
    {
        if (_icon is null) return;
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Warning);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };

        menu.Items.Add(Item("Nueva nota", () => _noteWindows.CreateNote(), bold: true));
        menu.Items.Add(Item("Buscar notas…", _windows.ShowSearch));
        menu.Items.Add(Item("Papelera…", _windows.ShowTrash));
        menu.Items.Add(new ToolStripSeparator());

        _toggleItem = Item(string.Empty, _noteWindows.ToggleAll);
        _presentationItem = Item("Modo presentación", () => _noteWindows.SetPresentationMode(!_noteWindows.IsPresentationMode));
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_presentationItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Item("Exportar notas…", () => _ = _importExport.ExportAsync()));
        menu.Items.Add(Item("Importar notas…", () => _ = _importExport.ImportAsync()));
        menu.Items.Add(Item("Abrir carpeta de copias de seguridad", _importExport.OpenBackupFolder));
        menu.Items.Add(new ToolStripSeparator());

        _autostartItem = Item("Iniciar con Windows", () => _ = ToggleAutostartAsync());
        _privacyItem = Item("Ocultar al compartir pantalla", () => _ = _privacy.SetEnabledAsync(!_privacy.IsEnabled));
        menu.Items.Add(_autostartItem);
        menu.Items.Add(_privacyItem);
        menu.Items.Add(Item("Acerca de", ShowAbout));
        menu.Items.Add(Item("Salir", () => ExitRequested?.Invoke(this, EventArgs.Empty)));

        menu.Opening += (_, _) => RefreshChecks();
        return menu;
    }

    private void RefreshChecks()
    {
        if (_toggleItem is null) return;
        _toggleItem.Text = $"Ocultar/mostrar todas ({_notes.Settings.ToggleHotkey})";
        _presentationItem!.Checked = _noteWindows.IsPresentationMode;
        _autostartItem!.Checked = SafeIsAutostartEnabled();
        _privacyItem!.Checked = _privacy.IsEnabled;
    }

    private ToolStripMenuItem Item(string text, Action action, bool bold = false)
    {
        var item = new ToolStripMenuItem(text);
        // ToolStripItem no libera la fuente asignada: se guarda una sola y se libera en Dispose.
        if (bold) item.Font = _boldFont ??= new Font(item.Font, System.Drawing.FontStyle.Bold);
        // Las acciones se ejecutan en el Dispatcher de WPF para no anidar diálogos dentro del menú de WinForms.
        item.Click += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => Run(text, action));
        return item;
    }

    private void Run(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.Error($"Error en la acción de bandeja '{name}'", ex);
            _dialogs.ShowError("No se pudo completar la acción. El detalle se guardó en el registro.");
        }
    }

    private async Task ToggleAutostartAsync()
    {
        var enable = !SafeIsAutostartEnabled();
        try
        {
            if (enable) _autostart.Enable();
            else _autostart.Disable();
            _notes.Settings.StartWithWindows = enable;
            await _notes.SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            _log.Error("No se pudo cambiar el inicio con Windows", ex);
            _dialogs.ShowError("No se pudo cambiar el inicio automático con Windows.");
        }
    }

    private bool SafeIsAutostartEnabled()
    {
        try { return _autostart.IsEnabled; }
        catch (Exception ex) { _log.Warn("No se pudo consultar el autoarranque", ex); return false; }
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        _dialogs.Info(
            $"Kip {version}\n" +
            "Notas adhesivas siempre a la vista, como un post-it pegado en el monitor.\n\n" +
            $"• {_notes.Settings.ToggleHotkey}: ocultar o mostrar todas las notas.\n" +
            "• Doble clic en el icono de la bandeja: nota nueva.\n" +
            "• Las notas no aparecen al compartir pantalla (si la opción está activa).\n\n" +
            $"Sus notas se guardan sólo en este equipo:\n{_paths.DataDirectory}\n\n" +
            "──────────────\n" +
            "Creado por Carlos Daniel Rojas («cdrojas»),\n" +
            "con la ayuda de Claude, el asistente de IA de Anthropic.\n\n" +
            $"© {DateTime.Now.Year} Carlos Daniel Rojas",
            "Acerca de Kip");
    }

    private static Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Kip.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (IOException) { }
        catch (ArgumentException) { }
        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
            _icon = null;
        }
        _iconImage?.Dispose();
        _iconImage = null;
        _boldFont?.Dispose();
        _boldFont = null;
    }
}
