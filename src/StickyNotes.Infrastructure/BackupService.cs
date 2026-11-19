using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;
using StickyNotes.Core.Services;

namespace StickyNotes.Infrastructure;

/// <summary>
/// Backups diarios con retención, instantáneas de seguridad rotativas (<see cref="ISafetyBackup"/>)
/// y exportación/importación en el mismo formato que notes.json.
/// </summary>
public sealed class BackupService : IBackupService, ISafetyBackup
{
    /// <summary>Tamaño máximo de un archivo a importar.</summary>
    internal const long MaxImportBytes = 20L * 1024 * 1024;

    private readonly IAppPaths _paths;
    private readonly IClock _clock;

    public BackupService(IAppPaths paths, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(clock);
        _paths = paths;
        _clock = clock;
    }

    public async Task CreateDailyBackupIfNeededAsync(IReadOnlyCollection<Note> notes, int retentionDays, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var today = DateOnly.FromDateTime(_clock.Now.DateTime);
        var target = Path.Combine(_paths.BackupDirectory, BackupFiles.FileNameFor(today));
        if (!File.Exists(target))
        {
            await AtomicFile.WriteAllBytesAsync(target, NotesDocument.Serialize(notes), backupPath: null, ct).ConfigureAwait(false);
        }

        Prune(today, retentionDays);
    }

    public Task ExportAsync(IReadOnlyCollection<Note> notes, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return AtomicFile.WriteAllBytesAsync(filePath, NotesDocument.Serialize(notes), backupPath: null, ct);
    }

    public async Task<IReadOnlyList<Note>> ImportAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (new FileInfo(filePath).Length > MaxImportBytes)
        {
            throw new InvalidDataException(
                "El archivo es demasiado grande para ser una exportación de Kip (máximo 20 MB).");
        }

        try
        {
            return await NotesDocument.ReadAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("El archivo seleccionado no es una exportación válida de Kip.", ex);
        }
    }

    public Task<IReadOnlyList<string>> ListBackupsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> result = [.. BackupFiles.ListNewestFirst(_paths.BackupDirectory)];
        return Task.FromResult(result);
    }

    public Task CreateSafetySnapshotAsync(IReadOnlyCollection<Note> notes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return SafetySnapshots.WriteAsync(NotesDocument.Serialize(notes), _paths.BackupDirectory, ct);
    }

    /// <summary>Elimina los backups diarios cuya fecha (según el nombre) es anterior a hoy - retención.
    /// Las instantáneas de seguridad tienen su propia rotación y no se tocan aquí.</summary>
    private void Prune(DateOnly today, int retentionDays)
    {
        var cutoff = today.AddDays(-Math.Clamp(retentionDays, AppSettings.MinRetentionDays, AppSettings.MaxRetentionDays));
        foreach (var (path, date) in BackupFiles.ListWithDates(_paths.BackupDirectory))
        {
            if (date < cutoff)
            {
                File.Delete(path);
            }
        }
    }
}
