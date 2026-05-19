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

        _ = Task.Run(async () =>
        {
            try { await LoadLogsAsync(); }
            catch { }
        });
        StartWatching();
    }

    private async Task LoadLogsAsync()
    {
        LogEntries.Clear();
        if (string.IsNullOrEmpty(SelectedLogFile) || !File.Exists(SelectedLogFile))
            return;

        try
        {
            // Read with shared read access so Serilog can still write
            using var stream = new FileStream(SelectedLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = await reader.ReadToEndAsync();
            var lineArray = lines.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lineArray.Skip(Math.Max(0, lineArray.Length - 200)))
                LogEntries.Add(line);
        }
        catch (IOException)
        {
            // File is locked by Serilog, skip
        }
    }

    private void StartWatching()
    {
        _logWatcher = new FileSystemWatcher(_logDirectory, "log-*.txt")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _logWatcher.Changed += async (s, e) =>
        {
            try { await LoadLogsAsync(); }
            catch { }
        };
    }

    public void Dispose()
    {
        _logWatcher?.Dispose();
    }
}
