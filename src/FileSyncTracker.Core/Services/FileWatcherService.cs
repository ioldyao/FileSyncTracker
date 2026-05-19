using Microsoft.Extensions.Logging;

namespace FileSyncTracker.Core.Services;

public class FileWatcherService : IFileWatcherService, IDisposable
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Action<FileSystemEventArgs>> _callbacks = new();
    private readonly object _lock = new();

    public FileWatcherService(ILogger<FileWatcherService> logger)
    {
        _logger = logger;
    }

    public void Watch(string path, Action<FileSystemEventArgs> onChange)
    {
        lock (_lock)
        {
            if (_watchers.ContainsKey(path))
                Unwatch(path);

            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                _logger.LogWarning("Cannot watch path, directory does not exist: {Path}", path);
                return;
            }

            _logger.LogInformation("Watch: Setting up watcher for {Path} in directory {Dir}", path, dir);

            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            var targetName = Path.GetFileName(path);

            void OnChanged(object sender, FileSystemEventArgs e)
            {
                _logger.LogDebug("OnChanged: Name={Name}, FullPath={FullPath}", e.Name, e.FullPath);
                if (string.IsNullOrEmpty(targetName) || e.Name == targetName)
                    onChange(e);
            }

            void OnDeleted(object sender, FileSystemEventArgs e)
            {
                _logger.LogDebug("OnDeleted: Name={Name}, FullPath={FullPath}", e.Name, e.FullPath);
                if (string.IsNullOrEmpty(targetName) || e.Name == targetName)
                    onChange(e);
            }

            void OnRenamed(object sender, RenamedEventArgs e)
            {
                _logger.LogDebug("OnRenamed: OldName={OldName}, NewName={NewName}", e.OldName, e.Name);
                if (string.IsNullOrEmpty(targetName) || e.OldName == targetName)
                    onChange(new FileSystemEventArgs(WatcherChangeTypes.Renamed, dir, e.Name));
            }

            watcher.Changed += OnChanged;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += (s, e) => _logger.LogError(e.GetException(), "FileSystemWatcher error for {Path}", path);

            _watchers[path] = watcher;
            _callbacks[path] = onChange;

            _logger.LogDebug("Started watching: {Path}", path);
        }
    }

    public void Unwatch(string path)
    {
        lock (_lock)
        {
            if (_watchers.TryGetValue(path, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchers.Remove(path);
                _callbacks.Remove(path);
                _logger.LogDebug("Stopped watching: {Path}", path);
            }
        }
    }

    public void RebindPath(string oldPath, string newPath, Action<FileSystemEventArgs> onChange)
    {
        Unwatch(oldPath);
        Watch(newPath, onChange);
        _logger.LogInformation("Rebound watcher from {Old} to {New}", oldPath, newPath);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
            _callbacks.Clear();
        }
    }
}
