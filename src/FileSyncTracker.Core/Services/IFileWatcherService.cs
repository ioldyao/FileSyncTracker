namespace FileSyncTracker.Core.Services;

public interface IFileWatcherService
{
    void Watch(string path, Action<FileSystemEventArgs> onChange);
    void Unwatch(string path);
    void RebindPath(string oldPath, string newPath, Action<FileSystemEventArgs> onChange);
}
