using System.Windows;
using Microsoft.Win32;

namespace StickyNotes.App.Services;

/// <summary>Implementación WPF de <see cref="IDialogService"/>. Usa como propietario la ventana
/// activa de la app o, si no hay, una ventana invisible "topmost": así el diálogo nunca queda
/// escondido detrás de las notas (que son topmost).</summary>
public sealed class DialogService : IDialogService
{
    public bool Confirm(string message, string title = "Kip") =>
        WithOwner(owner => MessageBox.Show(owner, message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);

    public void Info(string message, string title = "Kip") =>
        WithOwner(owner => MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information));

    public void ShowError(string message, string title = "Kip") =>
        WithOwner(owner => MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning));

    public string? AskSaveFile(string filter, string defaultFileName) => WithOwner(owner =>
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = defaultFileName, AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    });

    public string? AskOpenFile(string filter) => WithOwner(owner =>
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    });

    private static T WithOwner<T>(Func<Window, T> show)
    {
        var active = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (active is not null) return show(active);

        var host = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Topmost = true,
            Width = 0,
            Height = 0,
            Left = SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2,
            Top = SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height / 2,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = true,
        };
        try
        {
            host.Show();
            host.Activate();
            return show(host);
        }
        finally
        {
            host.Close();
        }
    }

    private static void WithOwner(Action<Window> show) => WithOwner<bool>(w => { show(w); return true; });
}
