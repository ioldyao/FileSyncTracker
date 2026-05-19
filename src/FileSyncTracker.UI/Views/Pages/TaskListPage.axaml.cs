using Avalonia.Controls;
using Avalonia.Interactivity;
using FileSyncTracker.UI.ViewModels;
using FileSyncTracker.UI.Views;

namespace FileSyncTracker.UI.Views.Pages;

public partial class TaskListPage : UserControl
{
    public TaskListPage()
    {
        InitializeComponent();
        DataContext = App.Services?.GetService(typeof(TaskListViewModel));
    }

    private void OnAddTaskClick(object? sender, RoutedEventArgs e)
    {
        MainWindow.Instance.ShowPage("AddTask");
    }
}
