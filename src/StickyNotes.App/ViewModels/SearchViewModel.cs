using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.App.ViewModels;

/// <summary>Un resultado de búsqueda: título, fragmento y color.</summary>
public sealed record SearchResultItem(Guid Id, string Title, string Snippet, NoteColor Color)
{
    private const int SnippetLength = 90;

    public static SearchResultItem From(Note note)
    {
        var title = string.IsNullOrWhiteSpace(note.Title) ? "(Sin título)" : note.Title.Trim();
        var text = string.Join(" ", new[] { note.PlainText }
            .Concat(note.Checklist.Select(c => c.Text))
            .Where(s => !string.IsNullOrWhiteSpace(s)))
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length > SnippetLength) text = text[..SnippetLength].TrimEnd() + "…";
        return new SearchResultItem(note.Id, title, text, note.Color);
    }
}

/// <summary>Búsqueda en vivo sobre las notas activas.</summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly INoteService _notes;
    private readonly Action<Guid> _focusNote;
    private string _query = string.Empty;
    private SearchResultItem? _selected;

    public SearchViewModel(INoteService notes, Action<Guid> focusNote)
    {
        _notes = notes;
        _focusNote = focusNote;
        Refresh();
    }

    public ObservableCollection<SearchResultItem> Results { get; } = new();

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty)) Refresh();
        }
    }

    public SearchResultItem? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public bool HasNoResults => Results.Count == 0;

    /// <summary>Sin texto se listan todas las notas activas.</summary>
    public void Refresh()
    {
        var found = string.IsNullOrWhiteSpace(_query) ? _notes.ActiveNotes : _notes.Search(_query.Trim());
        Results.Clear();
        foreach (var note in found) Results.Add(SearchResultItem.From(note));
        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoResults));
    }

    [RelayCommand]
    private void Open(SearchResultItem? item)
    {
        if ((item ?? Selected) is { } target) _focusNote(target.Id);
    }
}
