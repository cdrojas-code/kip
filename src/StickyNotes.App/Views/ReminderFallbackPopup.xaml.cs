using System.Windows;
using StickyNotes.Core.Models;

namespace StickyNotes.App.Views;

/// <summary>Ventanita "topmost" abajo a la derecha que sustituye al toast de Windows cuando éste no está disponible.
/// Vista sencilla sin lógica propia: no justifica un ViewModel.</summary>
public partial class ReminderFallbackPopup : Window
{
    private const double ScreenMargin = 12;
    private readonly Action _openNote;

    /// <param name="isPrivate">Pantalla compartida o modo presentación: no se muestra título ni contenido.</param>
    public ReminderFallbackPopup(Note note, bool isPrivate, Action openNote)
    {
        _openNote = openNote;
        DataContext = note;
        InitializeComponent();

        if (isPrivate)
        {
            TitleText.Text = StickyNotes.Platform.ToastNotifier.PrivateReminderText;
            BodyText.Visibility = Visibility.Collapsed;
            DueText.Text = string.Empty;
        }
        else
        {
            TitleText.Text = string.IsNullOrWhiteSpace(note.Title) ? "Nota sin título" : note.Title;
            BodyText.Text = note.PlainText;
            BodyText.Visibility = string.IsNullOrWhiteSpace(note.PlainText) ? Visibility.Collapsed : Visibility.Visible;
            DueText.Text = note.Reminder is { } r ? $"Programado para el {r.DueAt.LocalDateTime:dd/MM/yyyy HH:mm}" : string.Empty;
        }

        Loaded += (_, _) => PlaceBottomRight();
    }

    /// <summary>Apila los avisos: cada uno se coloca encima de los ya abiertos.</summary>
    private void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        var stacked = Application.Current.Windows.OfType<ReminderFallbackPopup>()
            .Where(w => w != this && w.IsVisible)
            .Sum(w => w.ActualHeight + ScreenMargin);
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = Math.Max(area.Top, area.Bottom - ActualHeight - ScreenMargin - stacked);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        _openNote();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
