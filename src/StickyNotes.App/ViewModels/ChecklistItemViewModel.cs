using CommunityToolkit.Mvvm.ComponentModel;
using StickyNotes.Core.Models;

namespace StickyNotes.App.ViewModels;

/// <summary>Una tarea de la lista de la nota. Cada cambio se comunica a la nota propietaria.</summary>
public sealed class ChecklistItemViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public ChecklistItemViewModel(ChecklistItem model, Action onChanged)
    {
        Model = model;
        _onChanged = onChanged;
    }

    public ChecklistItem Model { get; }

    public string Text
    {
        get => Model.Text;
        set
        {
            if (Model.Text == value) return;
            Model.Text = value ?? string.Empty;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public bool IsDone
    {
        get => Model.IsDone;
        set
        {
            if (Model.IsDone == value) return;
            Model.IsDone = value;
            OnPropertyChanged();
            _onChanged();
        }
    }
}
