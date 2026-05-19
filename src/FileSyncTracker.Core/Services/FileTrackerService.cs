using FileSyncTracker.Core.Events;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Security;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FileSyncTracker.Core.Services;

public class FileTrackerService : IFileTrackerService, IDisposable
{
    private readonly ILogger<FileTrackerService> _logger;
    private readonly IEverythingService _everythingService;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly ITaskRepository _taskRepository;
    private readonly ISyncSchedulerService _syncSchedulerService;
    private readonly WebDavStorageService _webDavService;
    private readonly Dictionary<Guid, CancellationTokenSource> _trackingTokens = new();
    private readonly Dictionary<Guid, DateTime> _lastSyncTimes = new();
    private readonly HashSet<Guid> _syncingTasks = new();
    private readonly object _lock = new();

    public event EventHandler<FileMovedEvent>? FileMoved;
    public event EventHandler<SyncStatusChangedEvent>? StatusChanged;

    public FileTrackerService(
        ILogger<FileTrackerService> logger,
        IEverythingService everythingService,
        IFileWatcherService fileWatcherService,
        ITaskRepository taskRepository,
        ISyncSchedulerService syncSchedulerService,
        WebDavStorageService webDavService)
    {
        _logger = logger;
        _everythingService = everythingService;
        _fileWatcherService = fileWatcherService;
        _taskRepository = taskRepository;
        _syncSchedulerService = syncSchedulerService;
        _webDavService = webDavService;
    }

    public async Task StartTrackingAsync(SyncTask task)
    {
        if (task.Type != SyncTaskType.SingleFile) return;

        // 如果文件不存在，尝试用 Everything 查找
        if (!File.Exists(task.CurrentPath) && task.Identity != null)
        {
            _logger.LogInformation("File not found at {Path}, searching with Everything...", task.CurrentPath);

            // 等待 Everything 索引更新
            await Task.Delay(2000);

            if (await TryResolvePathAsync(task))
            {
                _logger.LogInformation("File found and resolved: {Path}", task.CurrentPath);
            }
            else
            {
                _logger.LogWarning("File not found by Everything for task {TaskId}", task.Id);

                // 如果配置了下载路径，尝试从云端下载
                if (!string.IsNullOrEmpty(task.DownloadPath))
                {
                    _logger.LogInformation("Trying to download from cloud to {DownloadPath}", task.DownloadPath);
                    if (await TryDownloadFromCloudAsync(task))
                    {
                        _logger.LogInformation("Downloaded file to {Path}", task.DownloadPath);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to download file for task {TaskId}", task.Id);
                    }
                }
            }
        }

        // 记录文件的 FileID
        if (File.Exists(task.CurrentPath))
        {
            task.Identity = FileIdentity.FromFile(task.CurrentPath);
            await _taskRepository.UpdateAsync(task);
            _logger.LogInformation("Recorded FileId={FileId} for {Path}", task.Identity.NtfsFileId, task.CurrentPath);
        }

        // 启动文件监控
        _fileWatcherService.Watch(task.CurrentPath, args => OnFileChanged(task, args));
        _logger.LogInformation("Started tracking file: {Path} (Task: {TaskId})", task.CurrentPath, task.Id);
    }

    public async Task StopTrackingAsync(Guid taskId)
    {
        lock (_lock)
        {
            if (_trackingTokens.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                _trackingTokens.Remove(taskId);
            }
        }

        var task = await _taskRepository.GetByIdAsync(taskId);
        if (task != null)
        {
            _fileWatcherService.Unwatch(task.CurrentPath);
            _logger.LogInformation("Stopped tracking: {Path} (Task: {TaskId})", task.CurrentPath, taskId);
        }
    }

    /// <summary>
    /// 用 Everything 搜索文件，然后与记录的 FileID 匹配
    /// </summary>
    public async Task<bool> TryResolvePathAsync(SyncTask task)
    {
        if (task.Identity == null) return false;

        // 标记为同步中，防止反馈循环
        lock (_lock)
        {
            _syncingTasks.Add(task.Id);
        }

        try
        {
            _logger.LogInformation("TryResolvePath: Searching Everything for {FileName}, FileId={FileId}",
                task.Identity.FileName, task.Identity.NtfsFileId);

            // 用 Everything 搜索
            var newPath = await _everythingService.FindFileAsync(task.Identity);

            if (newPath == null)
            {
                _logger.LogWarning("TryResolvePath: Everything not found or no match for {FileName}", task.Identity.FileName);
                return false;
            }

            // 找到了，更新路径
            var oldPath = task.CurrentPath;
            task.CurrentPath = newPath;
            task.PathIsValid = true;
            task.UpdatedAt = DateTime.Now;

            // 更新 FileID
            if (File.Exists(newPath))
                task.Identity = FileIdentity.FromFile(newPath);

            await _taskRepository.UpdateAsync(task);

            // 重新绑定监控
            _fileWatcherService.RebindPath(oldPath, newPath, args => OnFileChanged(task, args));

            // 触发同步
            try
            {
                await _syncSchedulerService.TriggerNowAsync(task.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync after file resolve for task {TaskId}", task.Id);
            }

            FileMoved?.Invoke(this, new FileMovedEvent
            {
                TaskId = task.Id,
                OldPath = oldPath,
                NewPath = newPath
            });

            _logger.LogInformation("File resolved: {Old} -> {New} (Task: {TaskId})", oldPath, newPath, task.Id);

            // 等待一段时间再允许事件，让云同步完成
            await Task.Delay(3000);

            return true;
        }
        finally
        {
            lock (_lock)
            {
                _syncingTasks.Remove(task.Id);
            }
        }
    }

    private void OnFileChanged(SyncTask task, FileSystemEventArgs args)
    {
        // 同步期间忽略所有事件，防止反馈循环
        lock (_lock)
        {
            if (_syncingTasks.Contains(task.Id))
            {
                _logger.LogDebug("Ignoring event during sync: {ChangeType} for {Path}", args.ChangeType, args.FullPath);
                return;
            }
        }

        if (args.ChangeType == WatcherChangeTypes.Deleted || args.ChangeType == WatcherChangeTypes.Renamed)
        {
            _ = HandleFileDisappeared(task);
            return;
        }

        if (args.ChangeType == WatcherChangeTypes.Changed)
        {
            _ = HandleFileModified(task);
        }
    }

    private async Task HandleFileModified(SyncTask task)
    {
        // 防抖：10秒内不重复触发
        lock (_lock)
        {
            if (_lastSyncTimes.TryGetValue(task.Id, out var lastSync)
                && (DateTime.Now - lastSync).TotalSeconds < 10)
                return;
            _lastSyncTimes[task.Id] = DateTime.Now;
            _syncingTasks.Add(task.Id);
        }

        try
        {
            _logger.LogInformation("File modified, syncing task {TaskId}", task.Id);

            // 更新 FileID
            if (File.Exists(task.CurrentPath))
            {
                task.Identity = FileIdentity.FromFile(task.CurrentPath);
                await _taskRepository.UpdateAsync(task);
            }

            await _syncSchedulerService.TriggerNowAsync(task.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync after file modification for task {TaskId}", task.Id);
        }
        finally
        {
            // 等待一段时间再允许事件，让云同步完成
            await Task.Delay(3000);
            lock (_lock)
            {
                _syncingTasks.Remove(task.Id);
            }
        }
    }

    private async Task HandleFileDisappeared(SyncTask task)
    {
        _logger.LogInformation("File disappeared: {Path} (Task: {TaskId})", task.CurrentPath, task.Id);

        task.PathIsValid = false;
        task.Status = SyncStatus.Tracking;
        StatusChanged?.Invoke(this, new SyncStatusChangedEvent
        {
            TaskId = task.Id,
            NewStatus = SyncStatus.Tracking
        });
        await _taskRepository.UpdateAsync(task);

        // 等 2 秒让 Everything 索引更新
        await Task.Delay(2000);

        // 用 Everything 搜索 + FileID 匹配
        if (await TryResolvePathAsync(task))
        {
            task.Status = SyncStatus.Idle;
            StatusChanged?.Invoke(this, new SyncStatusChangedEvent
            {
                TaskId = task.Id,
                NewStatus = SyncStatus.Idle
            });
            await _taskRepository.UpdateAsync(task);
        }
        else
        {
            _logger.LogWarning("File not found by Everything for task {TaskId}", task.Id);

            // 如果配置了下载路径，尝试从云端下载
            if (!string.IsNullOrEmpty(task.DownloadPath))
            {
                _logger.LogInformation("Trying to download from cloud to {DownloadPath}", task.DownloadPath);
                if (await TryDownloadFromCloudAsync(task))
                {
                    task.Status = SyncStatus.Idle;
                    task.PathIsValid = true;
                    StatusChanged?.Invoke(this, new SyncStatusChangedEvent
                    {
                        TaskId = task.Id,
                        NewStatus = SyncStatus.Idle
                    });

                    // 重新绑定监控
                    _fileWatcherService.Watch(task.CurrentPath, args => OnFileChanged(task, args));
                    await _taskRepository.UpdateAsync(task);
                    return;
                }
            }

            task.Status = SyncStatus.Error;
            task.LastError = "文件未找到，请确认 Everything 已启动或设置下载路径";
            StatusChanged?.Invoke(this, new SyncStatusChangedEvent
            {
                TaskId = task.Id,
                NewStatus = SyncStatus.Error,
                ErrorMessage = task.LastError
            });
            await _taskRepository.UpdateAsync(task);
        }
    }

    private async Task<bool> TryDownloadFromCloudAsync(SyncTask task)
    {
        if (task.StorageTargets == null || task.StorageTargets.Count == 0)
            return false;

        try
        {
            var settings = await ReadSettingsAsync();
            if (settings == null) return false;

            // Ensure download directory exists
            var downloadDir = Path.GetDirectoryName(task.DownloadPath);
            if (!string.IsNullOrEmpty(downloadDir) && !Directory.Exists(downloadDir))
                Directory.CreateDirectory(downloadDir);

            foreach (var target in task.StorageTargets)
            {
                try
                {
                    var config = ResolveConfig(settings, target);
                    if (config == null) continue;

                    var fileName = Path.GetFileName(task.CurrentPath);
                    var remoteFilePath = string.IsNullOrEmpty(target.RemotePath)
                        ? fileName
                        : $"{target.RemotePath.TrimStart('/')}/{fileName}";

                    await _webDavService.DownloadFileAsync(config, remoteFilePath, task.DownloadPath);

                    // Update task path to download path
                    task.CurrentPath = task.DownloadPath;
                    task.PathIsValid = true;
                    await _taskRepository.UpdateAsync(task);

                    _logger.LogInformation("Downloaded file from cloud: {Path}", task.DownloadPath);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download from target {Target}", target.ServerName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download from cloud");
        }

        return false;
    }

    private async Task<AppSettings?> ReadSettingsAsync()
    {
        var settingsPath = AppSettings.GetSettingsPath();
        if (!File.Exists(settingsPath)) return null;

        var raw = await File.ReadAllTextAsync(settingsPath);
        var json = raw.TrimStart();
        if (!json.StartsWith('{'))
        {
            var decrypted = SecureStorage.Decrypt(raw);
            if (decrypted.StartsWith('{'))
                json = decrypted;
        }

        return JsonSerializer.Deserialize<AppSettings>(json);
    }

    private static CloudStorageConfig? ResolveConfig(AppSettings settings, StorageTarget target)
    {
        return target.Type switch
        {
            StorageType.WebDAV => settings.WebDavServers
                .Where(s => s.Id == target.ServerId)
                .Select(s => new CloudStorageConfig
                {
                    StorageType = StorageType.WebDAV,
                    WebDavUrl = s.Url,
                    WebDavUsername = s.Username,
                    WebDavPassword = s.Password,
                    RemotePath = target.RemotePath
                }).FirstOrDefault(),
            _ => null
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var cts in _trackingTokens.Values)
                cts.Dispose();
            _trackingTokens.Clear();
        }
    }
}
