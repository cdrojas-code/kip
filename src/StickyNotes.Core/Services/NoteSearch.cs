using System.Globalization;
using System.Text;
using StickyNotes.Core.Models;

namespace StickyNotes.Core.Services;

/// <summary>Búsqueda de texto insensible a mayúsculas y tildes sobre título, texto y checklist.</summary>
internal static class NoteSearch
{
    /// <summary>
    /// Devuelve las notas cuyo contenido contiene todas las palabras de <paramref name="query"/> (AND),
    /// ordenadas por <see cref="Note.UpdatedAt"/> descendente. Consulta vacía = todas.
    /// </summary>
    public static IReadOnlyList<Note> Search(IEnumerable<Note> notes, string? query)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var terms = Normalize(query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return notes
            .Where(n => terms.Length == 0 || MatchesAll(BuildHaystack(n), terms))
            .OrderByDescending(n => n.UpdatedAt)
            .ToList();
    }

    /// <summary>Quita tildes/diacríticos y pasa a minúsculas invariantes.</summary>
    internal static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

#pragma warning disable CA1308 // Se normaliza para comparar, no para mostrar ni persistir.
        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
#pragma warning restore CA1308
    }

    private static string BuildHaystack(Note note)
    {
        var parts = new List<string> { note.Title, note.PlainText };
        parts.AddRange(note.Checklist.Select(i => i.Text));
        // Separador de línea para que una palabra no "salte" entre campos.
        return Normalize(string.Join('\n', parts));
    }

    private static bool MatchesAll(string haystack, string[] terms) =>
        terms.All(t => haystack.Contains(t, StringComparison.Ordinal));
}
