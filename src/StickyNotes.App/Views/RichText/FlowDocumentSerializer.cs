using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace StickyNotes.App.Views.RichText;

/// <summary>Convierte entre FlowDocument y el par (RichContent XAML, PlainText) de <c>Note</c>.</summary>
public static class FlowDocumentSerializer
{
    /// <summary>Serializa el documento. Si no hay texto, RichContent queda vacío.</summary>
    public static (string RichXaml, string PlainText) Save(FlowDocument document)
    {
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        var plain = range.Text.TrimEnd('\r', '\n', ' ');
        if (string.IsNullOrWhiteSpace(plain)) return (string.Empty, string.Empty);

        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Xaml);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return (reader.ReadToEnd(), plain);
    }

    /// <summary>Carga el contenido en el documento. Contenido malformado o no seguro → texto plano.
    /// Devuelve false si hubo que recurrir al texto plano.</summary>
    public static bool Load(FlowDocument document, string? richXaml, string? plainText)
    {
        document.Blocks.Clear();
        if (!string.IsNullOrWhiteSpace(richXaml) && FlowDocumentXamlSanitizer.IsSafe(richXaml))
        {
            try
            {
                var range = new TextRange(document.ContentStart, document.ContentEnd);
                using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(richXaml));
                range.Load(stream, DataFormats.Xaml);
                ClearInheritedFormatting(document.Blocks);
                return true;
            }
            catch (Exception)
            {
                // Cualquier fallo de contenido importado/dañado → texto plano. Una nota rota nunca debe
                // impedir abrir la aplicación.
                document.Blocks.Clear();
            }
        }

        LoadPlainText(document, plainText);
        return string.IsNullOrWhiteSpace(richXaml);
    }

    public static void LoadPlainText(FlowDocument document, string? text)
    {
        document.Blocks.Clear();
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines) document.Blocks.Add(new Paragraph(new Run(line)));
    }

    /// <summary>TextRange.Save escribe tamaño, fuente y color heredados; al cargar quedarían fijos.
    /// Se eliminan para que la letra base (Note.FontSize) siga mandando.</summary>
    private static void ClearInheritedFormatting(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks.ToList())
        {
            Clear(block);
            switch (block)
            {
                case Paragraph p: ClearInlines(p.Inlines); break;
                case Section s: ClearInheritedFormatting(s.Blocks); break;
                case List l:
                    foreach (var item in l.ListItems) { Clear(item); ClearInheritedFormatting(item.Blocks); }
                    break;
                case Table t:
                    foreach (var cell in t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells))
                    { Clear(cell); ClearInheritedFormatting(cell.Blocks); }
                    break;
            }
        }
    }

    private static void ClearInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines.ToList())
        {
            Clear(inline);
            if (inline is Span span) ClearInlines(span.Inlines);
        }
    }

    private static void Clear(TextElement element)
    {
        element.ClearValue(TextElement.FontSizeProperty);
        element.ClearValue(TextElement.FontFamilyProperty);
        element.ClearValue(TextElement.ForegroundProperty);
        element.ClearValue(TextElement.BackgroundProperty);
    }
}
