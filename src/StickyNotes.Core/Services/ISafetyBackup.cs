using StickyNotes.Core.Models;

namespace StickyNotes.Core.Services;

/// <summary>
/// Capacidad opcional de un <see cref="Abstractions.IBackupService"/>: instantáneas de seguridad
/// rotativas antes de operaciones de riesgo (importar, borrados masivos).
/// <see cref="NoteService"/> la usa si el servicio de backups la implementa.
/// </summary>
public interface ISafetyBackup
{
    /// <summary>Guarda una instantánea de seguridad (backups\notes-safety-*.json), conservando las más recientes.</summary>
    Task CreateSafetySnapshotAsync(IReadOnlyCollection<Note> notes, CancellationToken ct = default);
}
