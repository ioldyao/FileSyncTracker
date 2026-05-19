using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileSyncTracker.Core.Database;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Services;
using FileSyncTracker.UI.Services;
using FileSyncTracker.UI.ViewModels;
using FileSyncTracker.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Diagnostics;

namespace FileSyncTracker.UI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        SetupServices();

        try
        {
            LocalizationService.Instance.ApplyLanguage(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Localization] Failed to apply language: {ex}");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FileSyncTracker", "localization_error.txt"),
                ex.ToString());
        }
    }

    private void SetupServices()
    {
        var services = new ServiceCollection();

        // Logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FileSyncTracker", "logs", "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog());

        // Repositories
        services.AddSingleton<ITaskRepository, TaskRepository>();

        // Services
        services.AddSingleton<IEverythingService, EverythingService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        services.AddSingleton<IFileTrackerService, FileTrackerService>();
        services.AddSingleton<ISyncthingService, SyncthingService>();
        services.AddSingleton<ISyncSchedulerService, SyncSchedulerService>();
        services.AddSingleton<ICloudStorageService, WebDavStorageService>();
        services.AddSingleton<WebDavStorageService>();
        services.AddSingleton<OneDriveStorageService>();
        services.AddSingleton<S3StorageService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<TaskListViewModel>();
        services.AddTransient<AddTaskViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LogViewModel>();
        services.AddTransient<FilesViewModel>();

        Services = services.BuildServiceProvider();
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Ensure SQLite database is created
            try
            {
                using var db = new SyncStateDbContext();
                db.Database.EnsureCreated();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Database] Failed to initialize: {ex.Message}");
            }

            var mainWindow = new MainWindow
            {
                DataContext = Services?.GetService<MainWindowViewModel>()
            };
            desktop.MainWindow = mainWindow;

            // Re-start file tracking for existing SingleFile tasks
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000); // Wait for UI to initialize

                    var repo = Services?.GetService<ITaskRepository>();
                    var tracker = Services?.GetService<IFileTrackerService>();
                    if (repo != null && tracker != null)
                    {
                        var tasks = await repo.GetAllAsync();
                        foreach (var task in tasks.Where(t => t.Type == SyncTaskType.SingleFile && t.IsEnabled))
                        {
                            try
                            {
                                await tracker.StartTrackingAsync(task);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[FileTracker] Failed to re-start tracking for {task.Name}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FileTracker] Failed to initialize tracking: {ex.Message}");
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
