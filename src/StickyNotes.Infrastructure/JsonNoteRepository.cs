using System.Globalization;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.Infrastructure;

/// <summary>
/// Persiste las notas en notes.json con escritura atómica.
/// <list type="bullet">
/// <item>Bloqueos transitorios (antivirus, OneDrive…) se reintentan; si el archivo existe pero sigue sin poder
/// leerse, se lanza la excepción: nunca se devuelve una lista vacía que un guardado posterior escribiría encima.</item>
/// <item>Si el archivo está corrupto recurre a notes.json.bak y luego al backup diario más reciente,
/// conservando siempre una copia del archivo dañado (notes.corrupt-&lt;fecha&gt;.json).</item>
/// <item>Antes de un guardado que reduce el número de notas a menos de la mitad, copia el archivo actual
/// como instantánea de seguridad (<see cref="SafetySnapshots"/>).</item>
/// </list>
/// </summary>
public sealed class JsonNoteRepository : INoteRepository
{
    /// <summary>Por debajo de este número de notas previas no se crean instantáneas por borrado masivo.</summary>
    internal const int SafetyMinimumPreviousCount = 3;

    private readonly IAppPaths _paths;
    private readonly IoRetry _retry;

    /// <summary>Número de notas del último archivo leído o escrito (-1 = desconocido).</summary>
    private int _lastKnownCount = -1;

    public JsonNoteRepository(IAppPaths paths)
        : this(paths, IoRetry.Default)
    {
    }

    internal JsonNoteRepository(IAppPaths paths, IoRetry retry)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(retry);
        _paths = paths;
        _retry = retry;
    }

    private string BakFile => _paths.NotesFile + ".bak";

    public async Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_paths.NotesFile))
        {
            _lastKnownCount = 0;
            return [];
        }

        try
        {
            // Errores de E/S persistentes se propagan: no se puede afirmar que no haya notas.
            return Remember(await _retry.RunAsync(() => NotesDocument.ReadAsync(_paths.NotesFile, ct), ct).ConfigureAwait(false));
        }
        catch (InvalidDataException)
        {
            // Nunca destruir datos: el siguiente guardado sobrescribiría el archivo dañado.
            PreserveCorruptFile();
        }

        foreach (var candidate in FallbackCandidates())
        {
            try
            {
                return Remember(await _retry.RunAsync(() => NotesDocument.ReadAsync(candidate, ct), ct).ConfigureAwait(false));
            }
            catch (InvalidDataException)
            {
                // Probar el siguiente candidato.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Un candidato ilegible no impide probar los demás.
            }
        }

        // El archivo principal está corrupto (ya preservado) y no hay respaldo legible.
        _lastKnownCount = -1;
        return [];
    }

    public async Task SaveAllAsync(IReadOnlyCollection<Note> notes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notes);

        if (IsMassReduction(notes.Count) && File.Exists(_paths.NotesFile))
        {
            await _retry.RunAsync(() =>
            {
                SafetySnapshots.CopyFrom(_paths.NotesFile, _paths.BackupDirectory);
                return Task.CompletedTask;
            }, ct).ConfigureAwait(false);
        }

        var bytes = NotesDocument.Serialize(notes);
        await _retry.RunAsync(() => AtomicFile.WriteAllBytesAsync(_paths.NotesFile, bytes, BakFile, ct), ct).ConfigureAwait(false);
        _lastKnownCount = notes.Count;
    }

    private bool IsMassReduction(int newCount) =>
        _lastKnownCount >= SafetyMinimumPreviousCount && newCount * 2 < _lastKnownCount;

    private IReadOnlyList<Note> Remember(IReadOnlyList<Note> notes)
    {
        _lastKnownCount = notes.Count;
        return notes;
    }

    private IEnumerable<string> FallbackCandidates()
    {
        if (File.Exists(BakFile))
        {
            yield return BakFile;
        }

        foreach (var backup in BackupFiles.ListNewestFirst(_paths.BackupDirectory))
        {
            yield return backup;
        }
    }

    private void PreserveCorruptFile()
    {
        var directory = Path.GetDirectoryName(_paths.NotesFile)!;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(directory, $"notes.corrupt-{stamp}.json");
        for (var i = 1; File.Exists(target); i++)
        {
            target = Path.Combine(directory, $"notes.corrupt-{stamp}-{i}.json");
        }

        File.Copy(_paths.NotesFile, target);
    }
}
