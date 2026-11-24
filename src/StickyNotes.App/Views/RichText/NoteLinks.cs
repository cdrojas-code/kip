using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace StickyNotes.App.Views.RichText;

/// <summary>Hipervínculos dentro del texto de una nota: validación de direcciones, detección automática
/// de URLs y utilidades sobre <see cref="Hyperlink"/>. Sólo se admiten http, https y mailto: un vínculo
/// nunca debe poder abrir archivos locales ni ejecutar programas (el XAML puede venir de un archivo importado).</summary>
public static partial class NoteLinks
{
    /// <summary>Longitud máxima de una dirección.</summary>
    internal const int MaxUriLength = 2048;

    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':', '!', '?', ')', ']', '}', '\'', '"', '»', '”'];

    [GeneratedRegex(@"(?:https?://|www\.|mailto:)[^\s<>""]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    public static bool IsAllowedScheme(Uri uri) =>
        uri.IsAbsoluteUri &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto);

    /// <summary>Convierte lo que escribe el usuario ("www.x.com", "x.com/a", "https://…", "a@b.com")
    /// en una dirección válida y permitida.</summary>
    public static bool TryNormalize(string? input, out Uri uri)
    {
        uri = null!;
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > MaxUriLength || text.Any(char.IsWhiteSpace)) return false;

        if (!text.Contains("://", StringComparison.Ordinal) && !text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            // "a@b.com" → correo; cualquier otra cosa con un punto → web.
            if (text.Contains('@') && !text.Contains('/')) text = "mailto:" + text;
            else if (text.Contains('.')) text = "https://" + text;
            else return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || !IsAllowedScheme(parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeMailto && string.IsNullOrEmpty(parsed.Host)) return false;
        uri = parsed;
        return true;
    }

    /// <summary>Para el sanitizador: el valor guardado de NavigateUri debe ser una dirección permitida.</summary>
    public static bool IsSafeStoredUri(string value) =>
        value.Length <= MaxUriLength && Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsAllowedScheme(uri);

    /// <summary>Abre el vínculo con el programa predeterminado (navegador / correo).</summary>
    public static bool TryOpen(Uri? uri)
    {
        if (uri is null || !IsAllowedScheme(uri)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Hyperlink que contiene la posición, o null.</summary>
    public static Hyperlink? FindAt(TextPointer? position)
    {
        var current = position?.Parent;
        while (current is TextElement element)
        {
            if (element is Hyperlink link) return link;
            current = element.Parent;
        }
        return null;
    }

    /// <summary>Quita el vínculo conservando su texto y formato.</summary>
    public static void Unwrap(Hyperlink link)
    {
        var siblings = link.SiblingInlines;
        if (siblings is null) return;
        foreach (var child in link.Inlines.ToList())
        {
            link.Inlines.Remove(child);
            siblings.InsertBefore(link, child);
        }
        siblings.Remove(link);
    }

    /// <summary>Al pulsar espacio/Enter: convierte en vínculo la palabra que termina justo antes del
    /// separador recién escrito. Sólo esa palabra, para no volver a enlazar lo que el usuario desenlazó.</summary>
    public static bool LinkifyWordBefore(FlowDocument document, TextPointer caret)
    {
        var wordEnd = caret.GetNextInsertionPosition(LogicalDirection.Backward);
        if (wordEnd is null || FindAt(wordEnd) is not null) return false;

        var before = wordEnd.GetTextInRun(LogicalDirection.Backward);
        var wordStart = before.Length;
        while (wordStart > 0 && !char.IsWhiteSpace(before[wordStart - 1])) wordStart--;
        if (wordStart == before.Length) return false;

        var from = wordEnd.GetPositionAtOffset(wordStart - before.Length);
        return from is not null && Linkify(document, from, wordEnd);
    }

    /// <summary>Convierte en vínculos las URLs escritas como texto (fuera de vínculos existentes) que
    /// estén completamente dentro de [from, to] (todo el documento si son null). Devuelve true si cambió algo.</summary>
    public static bool Linkify(FlowDocument document, TextPointer? from = null, TextPointer? to = null)
    {
        var found = new List<(TextPointer Start, TextPointer End, Uri Uri)>();
        foreach (var run in EnumerateRuns(document.Blocks))
        {
            if (FindAt(run.ContentStart) is not null) continue;
            foreach (Match match in UrlPattern().Matches(run.Text))
            {
                var value = match.Value.TrimEnd(TrailingPunctuation);
                if (!TryNormalize(value, out var uri)) continue;
                var start = run.ContentStart.GetPositionAtOffset(match.Index);
                var end = start?.GetPositionAtOffset(value.Length);
                if (start is null || end is null) continue;
                if (from is not null && start.CompareTo(from) < 0) continue;
                if (to is not null && end.CompareTo(to) > 0) continue;
                found.Add((start, end, uri));
            }
        }

        // De atrás hacia delante: los TextPointer siguen siendo válidos, pero así se evita reordenar elementos ya creados.
        for (var i = found.Count - 1; i >= 0; i--)
        {
            var (start, end, uri) = found[i];
            try { _ = new Hyperlink(start, end) { NavigateUri = uri }; }
            catch (InvalidOperationException) { /* rango no enlazable (p. ej. cruza elementos): se deja como texto */ }
            catch (ArgumentException) { }
        }
        return found.Count > 0;
    }

    private static IEnumerable<Run> EnumerateRuns(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks.ToList())
        {
            switch (block)
            {
                case Paragraph p:
                    foreach (var run in EnumerateRuns(p.Inlines)) yield return run;
                    break;
                case Section s:
                    foreach (var run in EnumerateRuns(s.Blocks)) yield return run;
                    break;
                case List l:
                    foreach (var item in l.ListItems.ToList())
                        foreach (var run in EnumerateRuns(item.Blocks)) yield return run;
                    break;
                case Table t:
                    foreach (var cell in t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).ToList())
                        foreach (var run in EnumerateRuns(cell.Blocks)) yield return run;
                    break;
            }
        }
    }

    private static IEnumerable<Run> EnumerateRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines.ToList())
        {
            if (inline is Run run) yield return run;
            else if (inline is Span span and not Hyperlink)
                foreach (var nested in EnumerateRuns(span.Inlines)) yield return nested;
        }
    }
}
