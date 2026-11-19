using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StickyNotes.App.Services;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.App.ViewModels;

public sealed record TrashItem(Guid Id, string Title, string Snippet, string DeletedText, NoteColor Color)
{
    public static TrashItem From(Note note)
    {
        var s = SearchResultItem.From(note);
        var deleted = note.DeletedAt is { } d ? $"Eliminada el {d.LocalDateTime:dd/MM/yyyy HH:mm}" : string.Empty;
        return new TrashItem(note.Id, s.Title, s.Snippet, deleted, note.Color);
    }
}

/// <summary>Papelera: restaurar, eliminar definitivamente o vaciar.</summary>
public sealed partial class TrashViewModel : ObservableObject, IDisposable
{
    private readonly INoteService _notes;
    private readonly IDialogService _dialogs;
    private TrashItem? _selected;

    public TrashViewModel(INoteService notes, IDialogService dialogs)
    {
        _notes = notes;
        _dialogs = dialogs;
        _notes.NoteShown += OnNotesChanged;
        _notes.NoteHidden += OnNotesChanged;
        Refresh();
    }

    public ObservableCollection<TrashItem> Items { get; } = new();

    public TrashItem? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            RestoreCommand.NotifyCanExecuteChanged();
            DeletePermanentlyCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsEmpty => Items.Count == 0;

    public string RetentionText =>
        $"Las notas de la papelera se eliminan automáticamente a los {_notes.Settings.TrashRetentionDays} días.";

    public void Refresh()
    {
        var selectedId = Selected?.Id;
        Items.Clear();
        foreach (var note in _notes.TrashedNotes.OrderByDescending(n => n.DeletedAt)) Items.Add(TrashItem.From(note));
        Selected = Items.FirstOrDefault(i => i.Id == selectedId) ?? Items.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        EmptyTrashCommand.NotifyCanExecuteChanged();
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Restore()
    {
        if (Selected is not { } item) return;
        _notes.Restore(item.Id);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeletePermanently()
    {
        if (Selected is not { } item) return;
        if (!_dialogs.Confirm($"¿Eliminar definitivamente «{item.Title}»? Esta acción no se puede deshacer.",
                "Eliminar definitivamente")) return;
        _notes.DeletePermanently(item.Id);
        Refresh();
    }

    private bool CanEmpty() => Items.Count > 0;

    [RelayCommand(CanExecute = nameof(CanEmpty))]
    private void EmptyTrash()
    {
        if (!_dialogs.Confirm($"¿Vaciar la papelera? Se eliminarán definitivamente {Items.Count} nota(s).",
                "Vaciar papelera")) return;
        _notes.EmptyTrash();
        Refresh();
    }

    private void OnNotesChanged(object? sender, NoteEventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);

    public void Dispose()
    {
        _notes.NoteShown -= OnNotesChanged;
        _notes.NoteHidden -= OnNotesChanged;
    }
}
