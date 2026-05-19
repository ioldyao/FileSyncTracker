using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Security;
using Microsoft.Extensions.Logging;
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

        // Remove existing schedule
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
            _logger.LogInformation("TriggerNowAsync: Task {TaskId} has {Count} storage targets", taskId, task.StorageTargets?.Count ?? 0);

            if (task.StorageTargets != null && task.StorageTargets.Count > 0)
            {
                // Sync via configured cloud storage targets
                var settings = await ReadSettingsAsync();
                _logger.LogInformation("TriggerNowAsync: Settings loaded: {IsNull}", settings == null);

                if (settings != null)
                {
                    foreach (var target in task.StorageTargets)
                    {
                        try
                        {
                            _logger.LogInformation("TriggerNowAsync: Syncing to {Target} (Type={Type}, ServerId={ServerId}, RemotePath='{RemotePath}')",
                                target.ServerName, target.Type, target.ServerId, target.RemotePath);

                            var config = ResolveConfig(settings, target);
                            if (config == null)
                            {
                                _logger.LogWarning("TriggerNowAsync: ResolveConfig returned null for {Target}", target.ServerName);
                                continue;
                            }

                            _logger.LogInformation("TriggerNowAsync: Config resolved, calling {Service}", target.Type);

                            var service = GetCloudService(target.Type);
                            var remoteDir = target.RemotePath.TrimStart('/');

                            // Skip if local file hasn't changed since last sync
                            if (task.Type == SyncTaskType.SingleFile && File.Exists(task.CurrentPath))
                            {
                                var localTime = File.GetLastWriteTimeUtc(task.CurrentPath);
                                var lastSync = task.LastSyncTime?.ToUniversalTime() ?? DateTime.MinValue;
                                if (lastSync > DateTime.MinValue && localTime <= lastSync)
                                {
                                    _logger.LogInformation("Skipping upload: no changes since last sync (Local={LocalTime:u}, LastSync={LastSync:u})",
                                        localTime, lastSync);
                                    continue;
                                }
                            }

                            if (task.Type == SyncTaskType.Folder)
                                await service.UploadFolderAsync(config, task.CurrentPath, remoteDir);
                            else
                                await service.UploadFileAsync(config, task.CurrentPath, remoteDir);

                            _logger.LogInformation("Synced task {TaskId} to {Target}", taskId, target.ServerName);
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
                    _logger.LogWarning("TriggerNowAsync: Settings is null, cannot sync");
                    task.Status = SyncStatus.Error;
                    task.LastError = "Failed to load settings";
                }
            }
            else
            {
                _logger.LogWarning("TriggerNowAsync: No storage targets, falling back to Syncthing");
                // Fallback to Syncthing
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
        _logger.LogInformation("Manually triggered sync for task {TaskId}", taskId);
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
                var decrypted = Security.SecureStorage.Decrypt(raw);
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
