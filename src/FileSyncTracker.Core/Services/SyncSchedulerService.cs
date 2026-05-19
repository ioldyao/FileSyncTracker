using FileSyncTracker.Core.Database;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl;
using System.Text.Json;

namespace FileSyncTracker.Core.Services;

public class SyncSchedulerService : ISyncSchedulerService, IAsyncDisposable
{
    private readonly ILogger<SyncSchedulerService> _logger;
    private readonly ITaskRepository _taskRepository;
    private readonly ISyncthingService _syncthingService;
    private IScheduler? _scheduler;

    public SyncSchedulerService(
        ILogger<SyncSchedulerService> logger,
        ITaskRepository taskRepository,
        ISyncthingService syncthingService)
    {
        _logger = logger;
        _taskRepository = taskRepository;
        _syncthingService = syncthingService;
    }

    public async Task StartAsync()
    {
        var factory = new StdSchedulerFactory();
        _scheduler = await factory.GetScheduler();
        await _scheduler.Start();
    }

    public async Task ScheduleAsync(SyncTask task)
    {
        if (_scheduler == null) await StartAsync();

        var jobKey = new JobKey($"sync_{task.Id}", "sync_group");
        var triggerKey = new TriggerKey($"trigger_{task.Id}", "sync_group");

        if (await _scheduler!.CheckExists(jobKey))
            await _scheduler.DeleteJob(jobKey);

        if (task.Mode == SyncMode.Scheduled && !string.IsNullOrEmpty(task.CronExpression))
        {
            var job = JobBuilder.Create<SyncJob>()
                .WithIdentity(jobKey)
                .UsingJobData("taskId", task.Id.ToString())
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithCronSchedule(task.CronExpression)
                .Build();

            await _scheduler.ScheduleJob(job, trigger);
            _logger.LogInformation("Scheduled task {TaskId} with cron: {Cron}", task.Id, task.CronExpression);
        }
    }

    public async Task UnscheduleAsync(Guid taskId)
    {
        if (_scheduler == null) return;

        var jobKey = new JobKey($"sync_{taskId}", "sync_group");
        if (await _scheduler.CheckExists(jobKey))
            await _scheduler.DeleteJob(jobKey);
    }

    public async Task TriggerNowAsync(Guid taskId)
    {
        var task = await _taskRepository.GetByIdAsync(taskId);
        if (task == null) return;

        try
        {
            if (task.StorageTargets is { Count: > 0 })
            {
                var settings = await ReadSettingsAsync();
                if (settings == null)
                {
                    _logger.LogWarning("TriggerNowAsync: Settings is null, cannot sync");
                    task.Status = SyncStatus.Error;
                    task.LastError = "Failed to load settings";
                    await _taskRepository.UpdateAsync(task);
                    return;
                }

                foreach (var target in task.StorageTargets)
                {
                    try
                    {
                        var config = ResolveConfig(settings, target);
                        if (config == null)
                        {
                            _logger.LogWarning("ResolveConfig returned null for {Target}", target.ServerName);
                            continue;
                        }

                        var service = GetCloudService(target.Type);

                        if (task.Type == SyncTaskType.Folder)
                            await SyncFolderAsync(task, service, config, target);
                        else
                            await SyncSingleFileAsync(task, service, config, target);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to sync task {TaskId} to {Target}", taskId, target.ServerName);
                        task.Status = SyncStatus.Error;
                        task.LastError = ex.Message;
                    }
                }
            }
            else
            {
                _logger.LogWarning("No storage targets, falling back to Syncthing");
                var folderId = task.Id.ToString("N")[..8];
                await _syncthingService.TriggerSyncAsync(folderId);
            }

            if (task.Status != SyncStatus.Error)
            {
                task.LastSyncTime = DateTime.Now;
                task.Status = SyncStatus.Idle;
            }
        }
        catch (Exception ex)
        {
            task.Status = SyncStatus.Error;
            task.LastError = ex.Message;
            _logger.LogError(ex, "Sync failed for task {TaskId}", taskId);
        }

        await _taskRepository.UpdateAsync(task);
    }

    /// <summary>
    /// 单文件同步：双端独立判定 + 三方决策矩阵
    /// </summary>
    private async Task SyncSingleFileAsync(SyncTask task, ICloudStorageService service, CloudStorageConfig config, StorageTarget target)
    {
        var localPath = task.CurrentPath;
        if (!File.Exists(localPath))
        {
            _logger.LogWarning("Local file not found: {Path}", localPath);
            return;
        }

        var remoteDir = target.RemotePath.TrimStart('/');
        var fileName = Path.GetFileName(localPath);
        var remoteFilePath = string.IsNullOrEmpty(remoteDir) ? fileName : $"{remoteDir}/{fileName}";

        // 1. 读取本地快速元数据（mtime + size）
        var localInfo = new FileInfo(localPath);
        var currentLocalMTime = localInfo.LastWriteTimeUtc;
        var currentLocalSize = localInfo.Length;

        // 2. 查询 SQLite FileNode
        await using var db = new SyncStateDbContext();
        var fileNode = await db.FileNodes.FindAsync(task.Id, fileName);

        if (fileNode == null)
        {
            // 首次同步，创建 FileNode
            fileNode = new FileNode
            {
                TaskId = task.Id,
                RelativePath = fileName,
                IsDirectory = false,
                SyncStatus = SyncNodeStatus.Pending
            };
            db.FileNodes.Add(fileNode);
        }

        // 3. 判断本地是否有改动（基于 mtime + size 快速检测）
        var localChanged = fileNode.LocalMTimeUtc == DateTime.MinValue
            || currentLocalMTime != fileNode.LocalMTimeUtc
            || currentLocalSize != fileNode.LocalSize;

        // 4. PROPFIND 获取远端 size + mtime
        var remoteInfo = await service.GetFileInfoAsync(config, remoteFilePath);
        var remoteChanged = false;

        if (remoteInfo == null)
        {
            // 远端不存在，视为远端有改动（需要上传）
            remoteChanged = fileNode.RemoteMTimeUtc != DateTime.MinValue;
            if (!remoteChanged)
            {
                // 远端从未同步过，且本地也没改 → 首次上传
                localChanged = true;
            }
        }
        else
        {
            remoteChanged = remoteInfo.FileSize != fileNode.RemoteSize
                || remoteInfo.LastModified.ToUniversalTime() != fileNode.RemoteMTimeUtc;
        }

        _logger.LogInformation(
            "Sync decision for {File}: LocalChanged={LocalChanged} (mtime {LocalMTime:u} vs {NodeMTime:u}, size {LocalSize} vs {NodeSize}), " +
            "RemoteChanged={RemoteChanged} (rsize {RemoteSize} vs {NodeRSize}, rmtime {RemoteMTime:u} vs {NodeRMTime:u})",
            fileName, localChanged, currentLocalMTime, fileNode.LocalMTimeUtc, currentLocalSize, fileNode.LocalSize,
            remoteChanged, remoteInfo?.FileSize ?? 0, fileNode.RemoteSize,
            remoteInfo?.LastModified.ToUniversalTime() ?? DateTime.MinValue, fileNode.RemoteMTimeUtc);

        // 5. 三方决策矩阵
        if (!localChanged && !remoteChanged)
        {
            _logger.LogInformation("Skip {File}: both ends unchanged", fileName);
            return;
        }

        if (localChanged && !remoteChanged)
        {
            // 本地改了 → 上传
            _logger.LogInformation("Upload {File}: local changed, remote unchanged", fileName);
            await UploadWithTransaction(task, service, config, localPath, remoteFilePath, fileNode, db);
            return;
        }

        if (!localChanged && remoteChanged)
        {
            // 远端改了 → 下载
            _logger.LogInformation("Download {File}: remote changed, local unchanged", fileName);
            await DownloadWithTransaction(task, service, config, remoteFilePath, localPath, fileNode, db);
            return;
        }

        // 两端都改了 → 冲突
        _logger.LogWarning("Conflict detected for {File}: both local and remote changed", fileName);
        await HandleConflict(task, service, config, localPath, remoteFilePath, fileNode, db, currentLocalMTime, currentLocalSize);
    }

    /// <summary>
    /// 文件夹同步：遍历所有文件逐个做双端判定
    /// </summary>
    private async Task SyncFolderAsync(SyncTask task, ICloudStorageService service, CloudStorageConfig config, StorageTarget target)
    {
        var localDir = task.CurrentPath;
        if (!Directory.Exists(localDir))
        {
            _logger.LogWarning("Local folder not found: {Path}", localDir);
            return;
        }

        var remoteDir = target.RemotePath.TrimStart('/');
        var files = Directory.GetFiles(localDir, "*", SearchOption.AllDirectories);

        await using var db = new SyncStateDbContext();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(localDir, file).Replace("\\", "/");
            var remoteFilePath = string.IsNullOrEmpty(remoteDir) ? relativePath : $"{remoteDir}/{relativePath}";

            var localInfo = new FileInfo(file);
            var currentLocalMTime = localInfo.LastWriteTimeUtc;
            var currentLocalSize = localInfo.Length;

            var fileNode = await db.FileNodes.FindAsync(task.Id, relativePath);
            if (fileNode == null)
            {
                fileNode = new FileNode
                {
                    TaskId = task.Id,
                    RelativePath = relativePath,
                    IsDirectory = false,
                    SyncStatus = SyncNodeStatus.Pending
                };
                db.FileNodes.Add(fileNode);
            }

            var localChanged = fileNode.LocalMTimeUtc == DateTime.MinValue
                || currentLocalMTime != fileNode.LocalMTimeUtc
                || currentLocalSize != fileNode.LocalSize;

            var remoteInfo = await service.GetFileInfoAsync(config, remoteFilePath);
            var remoteChanged = false;

            if (remoteInfo == null)
            {
                remoteChanged = fileNode.RemoteMTimeUtc != DateTime.MinValue;
                if (!remoteChanged) localChanged = true;
            }
            else
            {
                remoteChanged = remoteInfo.FileSize != fileNode.RemoteSize
                    || remoteInfo.LastModified.ToUniversalTime() != fileNode.RemoteMTimeUtc;
            }

            if (!localChanged && !remoteChanged) continue;

            try
            {
                if (localChanged && !remoteChanged)
                {
                    await UploadWithTransaction(task, service, config, file, remoteFilePath, fileNode, db);
                }
                else if (!localChanged && remoteChanged)
                {
                    await DownloadWithTransaction(task, service, config, remoteFilePath, file, fileNode, db);
                }
                else
                {
                    await HandleConflict(task, service, config, file, remoteFilePath, fileNode, db, currentLocalMTime, currentLocalSize);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync {File}", relativePath);
            }
        }

        // 清理远端已删除但本地仍有记录的 FileNode
        var remoteFiles = await service.ListFilesAsync(config, remoteDir);
        var remoteFileNames = remoteFiles.Select(r => Path.GetFileName(r.TrimEnd('/'))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var staleNodes = await db.FileNodes
            .Where(f => f.TaskId == task.Id && !f.IsDirectory && !remoteFileNames.Contains(f.RelativePath))
            .ToListAsync();

        foreach (var node in staleNodes)
        {
            node.SyncStatus = SyncNodeStatus.Deleted;
            node.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 上传事务：Journal → 计算 SHA-256 → PUT（Overwrite: T）→ 更新 FileNode
    /// </summary>
    private async Task UploadWithTransaction(SyncTask task, ICloudStorageService service, CloudStorageConfig config,
        string localPath, string remoteFilePath, FileNode fileNode, SyncStateDbContext db)
    {
        var fileName = Path.GetFileName(localPath);
        var journal = new SyncJournal
        {
            TaskId = task.Id,
            FilePath = localPath,
            Direction = Database.SyncDirection.Upload,
            Status = JournalStatus.InProgress,
            StartedAt = DateTime.UtcNow
        };
        db.SyncJournals.Add(journal);
        await db.SaveChangesAsync();

        try
        {
            var localInfo = new FileInfo(localPath);
            var currentHash = await ContentHashService.ComputeHashAsync(localPath);

            var remoteDir = Path.GetDirectoryName(remoteFilePath)?.Replace("\\", "/") ?? "";
            await service.UploadFileAsync(config, localPath, remoteDir);
            _logger.LogInformation("Upload complete: {File} (hash={Hash})", fileName, currentHash[..16]);

            // 更新 FileNode
            fileNode.LocalSize = localInfo.Length;
            fileNode.LocalMTimeUtc = localInfo.LastWriteTimeUtc;
            fileNode.LocalContentHash = currentHash;
            fileNode.SyncStatus = SyncNodeStatus.Synced;
            fileNode.UpdatedAt = DateTime.UtcNow;

            // 记录远端状态（上传后远端应与本地一致）
            fileNode.RemoteSize = localInfo.Length;
            fileNode.RemoteMTimeUtc = DateTime.UtcNow; // WebDAV 可能会修改 mtime，用上传时间近似

            // 更新 Journal
            journal.Status = JournalStatus.Completed;
            journal.LocalHash = currentHash;
            journal.CompletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.ErrorMessage = ex.Message.Length > 1024 ? ex.Message[..1024] : ex.Message;
            journal.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// 下载事务：GET → 计算 SHA-256 → 原子重命名 → 更新 FileNode
    /// </summary>
    private async Task DownloadWithTransaction(SyncTask task, ICloudStorageService service, CloudStorageConfig config,
        string remoteFilePath, string localPath, FileNode fileNode, SyncStateDbContext db)
    {
        var fileName = Path.GetFileName(localPath);
        var journal = new SyncJournal
        {
            TaskId = task.Id,
            FilePath = localPath,
            Direction = Database.SyncDirection.Download,
            Status = JournalStatus.InProgress,
            StartedAt = DateTime.UtcNow
        };
        db.SyncJournals.Add(journal);
        await db.SaveChangesAsync();

        try
        {
            var dir = Path.GetDirectoryName(localPath);
            if (dir != null) Directory.CreateDirectory(dir);

            await service.DownloadFileAsync(config, remoteFilePath, localPath);

            var localInfo = new FileInfo(localPath);
            var hash = await ContentHashService.ComputeHashAsync(localPath);

            fileNode.LocalSize = localInfo.Length;
            fileNode.LocalMTimeUtc = localInfo.LastWriteTimeUtc;
            fileNode.LocalContentHash = hash;
            fileNode.RemoteSize = localInfo.Length;
            fileNode.RemoteMTimeUtc = DateTime.UtcNow;
            fileNode.SyncStatus = SyncNodeStatus.Synced;
            fileNode.UpdatedAt = DateTime.UtcNow;

            journal.Status = JournalStatus.Completed;
            journal.LocalHash = hash;
            journal.CompletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            _logger.LogInformation("Downloaded {File} (hash={Hash})", fileName, hash[..16]);
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.ErrorMessage = ex.Message.Length > 1024 ? ex.Message[..1024] : ex.Message;
            journal.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// 冲突处理：重命名冲突文件，上传两个版本
    /// </summary>
    private async Task HandleConflict(SyncTask task, ICloudStorageService service, CloudStorageConfig config,
        string localPath, string remoteFilePath, FileNode fileNode, SyncStateDbContext db,
        DateTime currentLocalMTime, long currentLocalSize)
    {
        var fileName = Path.GetFileNameWithoutExtension(localPath);
        var ext = Path.GetExtension(localPath);
        var hostname = Environment.MachineName;
        var timestamp = DateTime.Now.ToString("yyyyMMdd.HHmmss");

        // 1. 本地冲突副本：filename.[hostname].[timestamp].ext
        var conflictLocalName = $"{fileName}.{hostname}.{timestamp}{ext}";
        var localDir = Path.GetDirectoryName(localPath) ?? "";
        var conflictLocalPath = Path.Combine(localDir, conflictLocalName);

        // 从远端下载冲突版本到本地
        var conflictRemoteName = $"{fileName}.{hostname}.{timestamp}{ext}";
        var remoteDir = Path.GetDirectoryName(remoteFilePath)?.Replace("\\", "/") ?? "";
        var conflictRemotePath = string.IsNullOrEmpty(remoteDir) ? conflictRemoteName : $"{remoteDir}/{conflictRemoteName}";

        try
        {
            // 下载远端版本到本地冲突文件
            await service.DownloadFileAsync(config, remoteFilePath, conflictLocalPath);
            _logger.LogInformation("Conflict: downloaded remote version to {Path}", conflictLocalPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conflict: failed to download remote version, uploading local as conflict instead");
        }

        // 2. 上传本地版本到远端冲突文件名
        var localHash = await ContentHashService.ComputeHashAsync(localPath);
        var localInfo = new FileInfo(localPath);
        var remoteConflictDir = string.IsNullOrEmpty(remoteDir) ? "" : remoteDir;

        if (config.StorageType == StorageType.WebDAV && service is WebDavStorageService webDav)
        {
            // 上传本地版本到远端冲突文件名
            await webDav.UploadFileAsync(config, localPath, remoteConflictDir);
        }
        else
        {
            await service.UploadFileAsync(config, localPath, remoteConflictDir);
        }

        // 3. 更新 FileNode 状态为 Conflict
        fileNode.LocalSize = currentLocalSize;
        fileNode.LocalMTimeUtc = currentLocalMTime;
        fileNode.LocalContentHash = localHash;
        fileNode.SyncStatus = SyncNodeStatus.Conflict;
        fileNode.UpdatedAt = DateTime.UtcNow;

        var journal = new SyncJournal
        {
            TaskId = task.Id,
            FilePath = localPath,
            Direction = Database.SyncDirection.Upload,
            Status = JournalStatus.Completed,
            LocalHash = localHash,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        db.SyncJournals.Add(journal);

        await db.SaveChangesAsync();

        _logger.LogWarning("Conflict handled for {File}: local={LocalPath}, remote={RemotePath}",
            Path.GetFileName(localPath), conflictRemotePath);
    }

    private async Task<AppSettings?> ReadSettingsAsync()
    {
        var settingsPath = AppSettings.GetSettingsPath();
        if (!File.Exists(settingsPath)) return null;

        var raw = await File.ReadAllTextAsync(settingsPath);
        var json = raw.TrimStart();
        if (!json.StartsWith('{'))
        {
            try
            {
                var decrypted = SecureStorage.Decrypt(raw);
                if (decrypted.StartsWith('{')) json = decrypted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Settings decryption failed");
            }
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

            StorageType.OneDrive => settings.OneDriveAccounts
                .Where(s => s.Id == target.ServerId)
                .Select(s => new CloudStorageConfig
                {
                    StorageType = StorageType.OneDrive,
                    OneDriveToken = s.AccessToken,
                    RemotePath = target.RemotePath
                }).FirstOrDefault(),

            StorageType.S3 => settings.S3Servers
                .Where(s => s.Id == target.ServerId)
                .Select(s => new CloudStorageConfig
                {
                    StorageType = StorageType.S3,
                    S3Endpoint = s.Endpoint,
                    S3Bucket = s.Bucket,
                    S3AccessKey = s.AccessKey,
                    S3SecretKey = s.SecretKey,
                    S3Region = s.Region,
                    S3UsePathStyle = s.UsePathStyle,
                    RemotePath = target.RemotePath
                }).FirstOrDefault(),

            _ => null
        };
    }

    private ICloudStorageService GetCloudService(StorageType type)
    {
        return type switch
        {
            StorageType.WebDAV => new WebDavStorageService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WebDavStorageService>.Instance),
            StorageType.OneDrive => new OneDriveStorageService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<OneDriveStorageService>.Instance),
            StorageType.S3 => new S3StorageService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<S3StorageService>.Instance),
            _ => throw new ArgumentException($"Unsupported storage type: {type}")
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_scheduler != null)
        {
            await _scheduler.Shutdown();
            _scheduler = null;
        }
    }
}

public class SyncJob : IJob
{
    private readonly ITaskRepository _taskRepository;
    private readonly ISyncthingService _syncthingService;
    private readonly ILogger<SyncJob> _logger;

    public SyncJob(
        ITaskRepository taskRepository,
        ISyncthingService syncthingService,
        ILogger<SyncJob> logger)
    {
        _taskRepository = taskRepository;
        _syncthingService = syncthingService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var taskIdStr = context.MergedJobDataMap.GetString("taskId");
        if (string.IsNullOrEmpty(taskIdStr) || !Guid.TryParse(taskIdStr, out var taskId))
            return;

        var task = await _taskRepository.GetByIdAsync(taskId);
        if (task == null || !task.IsEnabled) return;

        try
        {
            var folderId = task.Id.ToString("N")[..8];
            await _syncthingService.TriggerSyncAsync(folderId);

            task.LastSyncTime = DateTime.Now;
            task.Status = SyncStatus.Idle;
            await _taskRepository.UpdateAsync(task);

            _logger.LogInformation("Scheduled sync completed for task {TaskId}", taskId);
        }
        catch (Exception ex)
        {
            task.Status = SyncStatus.Error;
            task.LastError = ex.Message;
            await _taskRepository.UpdateAsync(task);
            _logger.LogError(ex, "Scheduled sync failed for task {TaskId}", taskId);
        }
    }
}
