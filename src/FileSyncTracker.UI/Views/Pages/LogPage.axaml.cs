using Avalonia.Controls;
using FileSyncTracker.UI.ViewModels;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.Views.Pages;

public partial class LogPage : UserControl
{
    public LogPage()
    {
        InitializeComponent();
        DataContext = App.Services?.GetService(typeof(LogViewModel));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is LogViewModel vm)
            await vm.InitializeAsync();
    }
}
