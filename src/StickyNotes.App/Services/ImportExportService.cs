using System.Diagnostics;
using StickyNotes.App.Logging;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.App.Services;

/// <summary>Exportar / importar notas a un .json elegido por el usuario y abrir la carpeta de copias.</summary>
public sealed class ImportExportService
{
    private const string Filter = "Notas (*.json)|*.json";

    /// <summary>Límite de tamaño del archivo a importar (se lee entero en memoria).</summary>
    private const long MaxImportBytes = 50L * 1024 * 1024;

    private readonly INoteService _notes;
    private readonly IBackupService _backups;
    private readonly IAppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly NoteWindowManager _noteWindows;
    private readonly FileLogger _log;

    public ImportExportService(INoteService notes, IBackupService backups, IAppPaths paths,
        IDialogService dialogs, NoteWindowManager noteWindows, FileLogger log)
    {
        _notes = notes;
        _backups = backups;
        _paths = paths;
        _dialogs = dialogs;
        _noteWindows = noteWindows;
        _log = log;
    }

    public async Task ExportAsync()
    {
        var path = _dialogs.AskSaveFile(Filter, $"notas-{DateTime.Now:yyyyMMdd}.json");
        if (path is null) return;
        try
        {
            _noteWindows.CommitAllPendingEdits();
            var notes = _notes.ActiveNotes.ToList();
            await _backups.ExportAsync(notes, path);
            _dialogs.Info($"Se exportaron {notes.Count} notas a:\n{path}");
        }
        catch (Exception ex)
        {
            _log.Error("Error al exportar notas", ex);
            _dialogs.ShowError($"No se pudieron exportar las notas.\n\n{ex.Message}");
        }
    }

    public async Task ImportAsync()
    {
        var path = _dialogs.AskOpenFile(Filter);
        if (path is null) return;
        try
        {
            if (new FileInfo(path).Length > MaxImportBytes)
            {
                _dialogs.ShowError($"El archivo es demasiado grande para ser una exportación de Kip (máximo {MaxImportBytes / (1024 * 1024)} MB).");
                return;
            }

            // Las ediciones en curso deben estar en las notas antes de comparar "más reciente gana".
            _noteWindows.CommitAllPendingEdits();
            var imported = await _backups.ImportAsync(path);
            var count = _notes.Import(imported);
            await _notes.FlushAsync();
            _dialogs.Info(count == 1 ? "Se importó 1 nota." : $"Se importaron {count} notas.");
        }
        catch (Exception ex)
        {
            _log.Error("Error al importar notas", ex);
            _dialogs.ShowError($"No se pudo importar el archivo. Compruebe que sea una exportación de Kip.\n\n{ex.Message}");
        }
    }

    public void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(_paths.BackupDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.BackupDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("No se pudo abrir la carpeta de copias", ex);
            _dialogs.ShowError($"No se pudo abrir la carpeta:\n{_paths.BackupDirectory}");
        }
    }
}
