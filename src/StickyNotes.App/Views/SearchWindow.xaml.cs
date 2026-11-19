using System.Windows;
using System.Windows.Input;
using StickyNotes.App.ViewModels;

namespace StickyNotes.App.Views;

/// <summary>Code-behind: sólo navegación con teclado (flechas desde la caja de búsqueda, Enter, Esc).</summary>
public partial class SearchWindow : Window
{
    private readonly SearchViewModel _viewModel;

    public SearchWindow(SearchViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => QueryBox.Focus();
        Activated += (_, _) => _viewModel.Refresh();
    }

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.OpenCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down when _viewModel.Results.Count > 0:
                ResultList.SelectedIndex = Math.Min(ResultList.SelectedIndex + 1, _viewModel.Results.Count - 1);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when _viewModel.Results.Count > 0:
                ResultList.SelectedIndex = Math.Max(ResultList.SelectedIndex - 1, 0);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                break;
        }
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _viewModel.OpenCommand.Execute(null);

    private void ResultList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _viewModel.OpenCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }
}
