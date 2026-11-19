using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StickyNotes.App.Services;
using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.App.ViewModels;

/// <summary>Envuelve un <see cref="Note"/> para su ventana. Toda modificación pasa por aquí y termina
/// en <see cref="INoteService.MarkChanged"/> (que guarda con debounce).</summary>
public sealed partial class NoteViewModel : ObservableObject
{
    public const double MinFontSize = 9;
    public const double MaxFontSize = 32;
    private const double FontStep = 2;

    public const string DeleteConfirmation =
        "¿Mover esta nota a la papelera? Podrá recuperarla desde la Papelera.";

    private readonly INoteService _notes;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;
    private readonly Action<Note> _requestNewNoteNear;

    private bool _isColorPopupOpen;
    private bool _isSizePopupOpen;
    private bool _isReminderPopupOpen;
    private bool _isBodyEmpty;

    public NoteViewModel(Note note, INoteService notes, IDialogService dialogs, IClock clock, Action<Note> requestNewNoteNear)
    {
        Note = note;
        _notes = notes;
        _dialogs = dialogs;
        _clock = clock;
        _requestNewNoteNear = requestNewNoteNear;
        _isBodyEmpty = string.IsNullOrWhiteSpace(note.PlainText);

        Checklist = new ObservableCollection<ChecklistItemViewModel>(note.Checklist.Select(Wrap));
        Checklist.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChecklist));
    }

    public Note Note { get; }
    public Guid Id => Note.Id;

    /// <summary>Pide a la vista enfocar una tarea concreta (tras agregar/eliminar con el teclado).</summary>
    public event EventHandler<ChecklistItemViewModel>? ChecklistFocusRequested;

    // ───────────── Título y texto ─────────────

    public string Title
    {
        get => Note.Title;
        set
        {
            if (Note.Title == value) return;
            Note.Title = value ?? string.Empty;
            OnPropertyChanged();
            Changed();
        }
    }

    public string RichContent => Note.RichContent;
    public string PlainText => Note.PlainText;

    /// <summary>Controla el texto de ayuda "Escriba aquí…". La vista lo actualiza en cada pulsación
    /// (sin esperar al debounce de guardado) para que nunca se superponga a lo que se escribe.</summary>
    public bool IsBodyEmpty { get => _isBodyEmpty; set => SetProperty(ref _isBodyEmpty, value); }

    /// <summary>Llamado por la vista (RichTextBox) con el documento ya serializado.</summary>
    public void UpdateContent(string richXaml, string plainText)
    {
        IsBodyEmpty = string.IsNullOrWhiteSpace(plainText);
        if (Note.RichContent == richXaml && Note.PlainText == plainText) return;
        Note.RichContent = richXaml;
        Note.PlainText = plainText;
        Changed();
    }

    // ───────────── Aspecto ─────────────

    public NoteColor Color => Note.Color;
    public NoteSize Size => Note.Size;
    public double Width => Note.Size.ToDimensions().Width;
    public double Height => Note.Size.ToDimensions().Height;
    public double FontSize => Note.FontSize;

    public static IReadOnlyList<NoteColor> AvailableColors { get; } = Enum.GetValues<NoteColor>();

    public bool IsColorPopupOpen { get => _isColorPopupOpen; set => SetProperty(ref _isColorPopupOpen, value); }
    public bool IsSizePopupOpen { get => _isSizePopupOpen; set => SetProperty(ref _isSizePopupOpen, value); }

    [RelayCommand]
    private void SetColor(NoteColor color)
    {
        IsColorPopupOpen = false;
        if (Note.Color == color) return;
        Note.Color = color;
        OnPropertyChanged(nameof(Color));
        Changed();
    }

    [RelayCommand]
    private void SetSize(NoteSize size)
    {
        IsSizePopupOpen = false;
        if (Note.Size == size) return;
        Note.Size = size;
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        Changed();
    }

    [RelayCommand]
    private void IncreaseFont() => SetFontSize(Note.FontSize + FontStep);

    [RelayCommand]
    private void DecreaseFont() => SetFontSize(Note.FontSize - FontStep);

    private void SetFontSize(double size)
    {
        size = Math.Clamp(size, MinFontSize, MaxFontSize);
        if (Math.Abs(Note.FontSize - size) < 0.01) return;
        Note.FontSize = size;
        OnPropertyChanged(nameof(FontSize));
        Changed();
    }

    // ───────────── Posición ─────────────

    public bool IsPositionLocked
    {
        get => Note.IsPositionLocked;
        set
        {
            if (Note.IsPositionLocked == value) return;
            Note.IsPositionLocked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LockToolTip));
            Changed();
        }
    }

    public string LockToolTip => IsPositionLocked ? "Desbloquear posición" : "Bloquear posición";

    public void UpdatePosition(double left, double top)
    {
        if (Math.Abs(Note.Left - left) < 0.5 && Math.Abs(Note.Top - top) < 0.5) return;
        Note.Left = left;
        Note.Top = top;
        Changed();
    }

    // ───────────── Lista de tareas ─────────────

    public ObservableCollection<ChecklistItemViewModel> Checklist { get; }
    public bool HasChecklist => Checklist.Count > 0;

    [RelayCommand]
    private void AddChecklistItem() => InsertChecklistItemAfter(null);

    /// <summary>Inserta una tarea vacía tras <paramref name="previous"/> (o al final) y pide el foco.</summary>
    public void InsertChecklistItemAfter(ChecklistItemViewModel? previous)
    {
        var index = previous is null ? Checklist.Count : Checklist.IndexOf(previous) + 1;
        if (index <= 0) index = Checklist.Count;

        var model = new ChecklistItem();
        Note.Checklist.Insert(Math.Min(index, Note.Checklist.Count), model);
        var vm = Wrap(model);
        Checklist.Insert(index, vm);
        Changed();
        ChecklistFocusRequested?.Invoke(this, vm);
    }

    /// <summary>Elimina la tarea y devuelve el foco a la anterior (si existe).</summary>
    public void RemoveChecklistItem(ChecklistItemViewModel item)
    {
        var index = Checklist.IndexOf(item);
        if (index < 0) return;
        Checklist.RemoveAt(index);
        Note.Checklist.Remove(item.Model);
        Changed();
        if (index > 0) ChecklistFocusRequested?.Invoke(this, Checklist[index - 1]);
    }

    private ChecklistItemViewModel Wrap(ChecklistItem item) => new(item, Changed);

    // ───────────── Recordatorio ─────────────

    public ReminderEditor ReminderEditor { get; } = new();

    public bool HasReminder => Note.Reminder is not null;

    public string ReminderText => Note.Reminder is { } r ? $"⏰ {r.DueAt.LocalDateTime:dd/MM HH:mm}" : string.Empty;

    /// <summary>Vencido (o en su hora) y aún no quitado: el distintivo se pinta en rojo.</summary>
    public bool IsReminderDue => Note.Reminder is { } r && r.DueAt <= _clock.Now;

    public bool IsReminderPopupOpen
    {
        get => _isReminderPopupOpen;
        set
        {
            if (value && !_isReminderPopupOpen) ReminderEditor.LoadFrom(Note.Reminder, _clock.Now);
            SetProperty(ref _isReminderPopupOpen, value);
        }
    }

    [RelayCommand]
    private void OpenReminder() => IsReminderPopupOpen = true;

    [RelayCommand]
    private void SaveReminder()
    {
        if (ReminderEditor.ToDueAt() is not { } due)
        {
            _dialogs.Info("Elija una fecha para el recordatorio.");
            return;
        }
        Note.Reminder = new Reminder { DueAt = due, IsNotified = false };
        IsReminderPopupOpen = false;
        RaiseReminderChanged();
        Changed();
    }

    [RelayCommand]
    private void ClearReminder()
    {
        IsReminderPopupOpen = false;
        if (Note.Reminder is null) return;
        Note.Reminder = null;
        RaiseReminderChanged();
        Changed();
    }

    /// <summary>Recalcula el estado "vencido" (lo llama periódicamente el administrador de ventanas).</summary>
    public void RefreshReminderState() => RaiseReminderChanged();

    private void RaiseReminderChanged()
    {
        OnPropertyChanged(nameof(HasReminder));
        OnPropertyChanged(nameof(ReminderText));
        OnPropertyChanged(nameof(IsReminderDue));
    }

    // ───────────── Acciones ─────────────

    [RelayCommand]
    private void NewNote() => _requestNewNoteNear(Note);

    [RelayCommand]
    private void Delete()
    {
        if (_notes.Settings.ConfirmDelete && !_dialogs.Confirm(DeleteConfirmation, "Eliminar nota")) return;
        _notes.MoveToTrash(Note.Id);
    }

    private void Changed() => _notes.MarkChanged(Note);
}
