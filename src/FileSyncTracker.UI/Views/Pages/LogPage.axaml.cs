using Avalonia.Controls;
using FileSyncTracker.UI.ViewModels;

namespace FileSyncTracker.UI.Views.Pages;

public partial class LogPage : UserControl
{
    public LogPage()
    {
        InitializeComponent();
        DataContext = App.Services?.GetService(typeof(LogViewModel));
    }
}
