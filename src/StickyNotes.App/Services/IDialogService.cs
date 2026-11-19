namespace StickyNotes.App.Services;

/// <summary>Mensajes y confirmaciones en español, siempre por encima de las notas.</summary>
public interface IDialogService
{
    bool Confirm(string message, string title = "Kip");

    void Info(string message, string title = "Kip");

    void ShowError(string message, string title = "Kip");

    /// <summary>Diálogo "Guardar como". Devuelve null si se cancela.</summary>
    string? AskSaveFile(string filter, string defaultFileName);

    /// <summary>Diálogo "Abrir". Devuelve null si se cancela.</summary>
    string? AskOpenFile(string filter);
}
