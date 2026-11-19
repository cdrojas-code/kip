using System.Xml;

namespace StickyNotes.App.Views.RichText;

/// <summary>Valida que el XAML guardado en <c>Note.RichContent</c> sólo contenga contenido de texto
/// (párrafos, listas, tablas, formato). El XAML puede venir de un archivo importado: sin esta
/// comprobación, <c>TextRange.Load</c> podría instanciar objetos arbitrarios (ObjectDataProvider…).</summary>
public static class FlowDocumentXamlSanitizer
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNs = "http://www.w3.org/2000/xmlns/";

    /// <summary>Tamaño máximo (caracteres) del XAML de una nota: evita DoS con archivos importados enormes.</summary>
    internal const int MaxLength = 1_000_000;

    /// <summary>Anidamiento máximo de elementos.</summary>
    internal const int MaxDepth = 64;

    // Sin Hyperlink: el RichTextBox no habilita documentos interactivos y un NavigateUri importado no aporta nada.
    private static readonly HashSet<string> AllowedElements = new(StringComparer.Ordinal)
    {
        "Section", "Paragraph", "Run", "Span", "Bold", "Italic", "Underline", "LineBreak",
        "List", "ListItem",
        "Table", "TableColumn", "TableRowGroup", "TableRow", "TableCell",
        "TextDecoration", "TextDecorationCollection",
    };

    public static bool IsSafe(string xaml)
    {
        if (xaml.Length > MaxLength) return false;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = false,
                MaxCharactersInDocument = MaxLength,
            };
            using var reader = XmlReader.Create(new StringReader(xaml), settings);
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.ProcessingInstruction:
                    case XmlNodeType.DocumentType:
                        return false;
                    case XmlNodeType.XmlDeclaration:
                        // El XAML se carga como bytes UTF-8: otra codificación declarada abriría una
                        // diferencia entre lo validado aquí y lo que interpreta el lector de XAML.
                        var encoding = reader.GetAttribute("encoding");
                        if (encoding is not null && !string.Equals(encoding, "utf-8", StringComparison.OrdinalIgnoreCase)) return false;
                        break;
                    case XmlNodeType.Element:
                        if (reader.Depth > MaxDepth) return false;
                        if (!IsAllowedElement(reader) || !AreAttributesSafe(reader)) return false;
                        break;
                }
            }
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsAllowedElement(XmlReader reader)
    {
        if (reader.NamespaceURI != PresentationNs) return false;

        // Elementos de propiedad, p. ej. <Run.TextDecorations>: el "dueño" también debe estar permitido.
        var name = reader.LocalName;
        var dot = name.IndexOf('.');
        return AllowedElements.Contains(dot < 0 ? name : name[..dot]);
    }

    private static bool AreAttributesSafe(XmlReader reader)
    {
        if (!reader.MoveToFirstAttribute()) return true;
        try
        {
            do
            {
                var ns = reader.NamespaceURI;
                if (ns.Length != 0 && ns != XmlNs && ns != XmlnsNs) return false;
                if (ns == XmlnsNs && reader.Value != PresentationNs) return false;

                // Nada de extensiones de marcado ({x:Static}, {Binding}…), salvo el escape "{}".
                var value = reader.Value.TrimStart();
                if (value.StartsWith('{') && !value.StartsWith("{}", StringComparison.Ordinal)) return false;
            }
            while (reader.MoveToNextAttribute());
            return true;
        }
        finally
        {
            reader.MoveToElement();
        }
    }
}
