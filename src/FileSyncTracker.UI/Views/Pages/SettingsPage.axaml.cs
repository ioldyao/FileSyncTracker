using Avalonia.Controls;
using Avalonia.Interactivity;
using FileSyncTracker.UI.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.Views.Pages;

public partial class SettingsPage : UserControl
{
    private SettingsViewModel? _vm;

    public SettingsPage()
    {
        InitializeComponent();
        _vm = App.Services?.GetService(typeof(SettingsViewModel)) as SettingsViewModel;
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (_vm != null)
            await _vm.InitializeAsync();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileSyncTracker", "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "crash.txt");

        try
        {
            File.AppendAllText(logFile, $"\n[{DateTime.Now}] Save clicked\n");

            if (_vm == null)
            {
                File.AppendAllText(logFile, $"[{DateTime.Now}] ViewModel is null!\n");
                return;
            }

            File.AppendAllText(logFile, $"[{DateTime.Now}] Calling SaveSettingsAsync...\n");
            await _vm.SaveSettingsAsync();
            File.AppendAllText(logFile, $"[{DateTime.Now}] Save completed OK\n");
        }
        catch (Exception ex)
        {
            var msg = $"[{DateTime.Now}] CRASH: {ex}\n";
            File.AppendAllText(logFile, msg);
            Debug.WriteLine(msg);
        }
    }
}
