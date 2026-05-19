using Avalonia.Controls;
using FileSyncTracker.UI.ViewModels;

namespace FileSyncTracker.UI.Views.Pages;

public partial class FilesPage : UserControl
{
    public FilesPage()
    {
        InitializeComponent();
        DataContext = App.Services?.GetService(typeof(FilesViewModel));
    }
}
