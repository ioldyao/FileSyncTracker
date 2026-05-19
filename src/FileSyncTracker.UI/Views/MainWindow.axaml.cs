using Avalonia.Controls;
using Avalonia.Interactivity;
using FileSyncTracker.UI.ViewModels;
using FileSyncTracker.UI.Views.Pages;
using System;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.Views;

public partial class MainWindow : Window
{
    private DashboardPage? _dashboardPage;
    private TaskListPage? _taskListPage;
    private AddTaskPage? _addTaskPage;
    private SettingsPage? _settingsPage;
    private LogPage? _logPage;

    private static MainWindow? _instance;
    public static MainWindow Instance => _instance ?? throw new InvalidOperationException("MainWindow not initialized");

    public MainWindow()
    {
        _instance = this;
        InitializeComponent();
        ShowPage("Dashboard");
    }

    private void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
            ShowPage(tag);
    }

    public async void ShowPage(string pageName)
    {
        UserControl? page = pageName switch
        {
            "Dashboard" => _dashboardPage ??= new DashboardPage(),
            "Tasks" => _taskListPage ??= new TaskListPage(),
            "AddTask" => _addTaskPage ??= new AddTaskPage(),
            "Settings" => _settingsPage ??= new SettingsPage(),
            "Logs" => _logPage ??= new LogPage(),
            _ => null
        };

        if (page == null) return;

        // Refresh data when navigating to existing pages
        if (pageName == "Tasks" && _taskListPage?.DataContext is TaskListViewModel tvm)
            await tvm.RefreshAsync();
        else if (pageName == "Dashboard" && _dashboardPage?.DataContext is DashboardViewModel dvm)
            await dvm.RefreshAsync();

        ContentPanel?.Children.Clear();
        ContentPanel?.Children.Add(page);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
