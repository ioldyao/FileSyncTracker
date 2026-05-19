using Avalonia.Controls;
using FileSyncTracker.UI.ViewModels;

namespace FileSyncTracker.UI.Views.Pages;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
        DataContext = App.Services?.GetService(typeof(DashboardViewModel));
    }
}
