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
    private FilesPage? _filesPage;
    private SettingsPage? _settingsPage;
    private LogPage? _logPage;

    private string _currentPage = "Dashboard";
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

    private void UpdateNavButtons()
    {
        // Find the nav StackPanel inside the first Grid row
        if (Content is not Grid rootGrid) return;
        if (rootGrid.Children[1] is not Grid mainGrid) return;
        if (mainGrid.Children[0] is not Border navBorder) return;
        if (navBorder.Child is not StackPanel navStack) return;

        foreach (var child in navStack.Children)
        {
            if (child is Button navBtn && navBtn.Tag is string tag)
            {
                if (tag == _currentPage)
                    navBtn.Classes.Add("active");
                else
                    navBtn.Classes.Remove("active");
            }
        }
    }

    public async void ShowPage(string pageName)
    {
        UserControl? page = pageName switch
        {
            "Dashboard" => _dashboardPage ??= new DashboardPage(),
            "Tasks" => _taskListPage ??= new TaskListPage(),
            "AddTask" => _addTaskPage ??= new AddTaskPage(),
            "Files" => _filesPage ??= new FilesPage(),
            "Settings" => _settingsPage ??= new SettingsPage(),
            "Logs" => _logPage ??= new LogPage(),
            _ => null
        };

        if (page == null) return;

        // Refresh data when navigating to existing pages
        if (pageName == "Tasks" && _taskListPage?.DataContext is TaskListViewModel tvm)
            await tvm.RefreshAsync();
        else if (pageName == "Files" && _filesPage?.DataContext is FilesViewModel fvm)
            await fvm.RefreshAsync();
        else if (pageName == "Dashboard" && _dashboardPage?.DataContext is DashboardViewModel dvm)
            await dvm.RefreshAsync();

        _currentPage = pageName;
        ContentPanel?.Children.Clear();
        ContentPanel?.Children.Add(page);

        UpdateNavButtons();
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
