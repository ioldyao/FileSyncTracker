using Avalonia.Controls;
using FileSyncTracker.UI.ViewModels;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.Views.Pages;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
        DataContext = App.Services?.GetService(typeof(DashboardViewModel));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is DashboardViewModel vm)
            await vm.InitializeAsync();
    }
}
