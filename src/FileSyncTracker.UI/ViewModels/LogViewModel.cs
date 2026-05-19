using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.ViewModels;

public partial class LogViewModel : ObservableObject
{
    private readonly string _logDirectory;
    private FileSystemWatcher? _logWatcher;

    public ObservableCollection<string> LogEntries { get; } = new();

    [ObservableProperty]
    private string _selectedLogFile = string.Empty;

    public LogViewModel()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileSyncTracker", "logs");
        Directory.CreateDirectory(_logDirectory);

        SelectedLogFile = Directory.GetFiles(_logDirectory, "log-*.txt")
            .OrderByDescending(f => f)
            .FirstOrDefault() ?? string.Empty;

        _ = LoadLogsAsync();
        StartWatching();
    }

    private async Task LoadLogsAsync()
    {
        LogEntries.Clear();
        if (string.IsNullOrEmpty(SelectedLogFile) || !File.Exists(SelectedLogFile))
            return;

        var lines = await File.ReadAllLinesAsync(SelectedLogFile);
        foreach (var line in lines.Skip(Math.Max(0, lines.Length - 200)))
            LogEntries.Add(line);
    }

    private void StartWatching()
    {
        _logWatcher = new FileSystemWatcher(_logDirectory, "log-*.txt")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _logWatcher.Changed += async (s, e) => await LoadLogsAsync();
    }

    public void Dispose()
    {
        _logWatcher?.Dispose();
    }
}
