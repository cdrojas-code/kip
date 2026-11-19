using System.Windows;
using StickyNotes.App.ViewModels;

namespace StickyNotes.App.Views;

public partial class TrashWindow : Window
{
    public TrashWindow(TrashViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Activated += (_, _) => viewModel.Refresh();
        Closed += (_, _) => viewModel.Dispose();
    }
}
