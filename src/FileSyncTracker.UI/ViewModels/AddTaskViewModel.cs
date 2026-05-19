using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Security;
using FileSyncTracker.Core.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.ViewModels;

public partial class AddTaskViewModel : ObservableObject
{
    public static SyncTask? EditingTask { get; set; }

    private readonly ITaskRepository _taskRepository;
    private readonly IFileTrackerService _fileTrackerService;
    private readonly ISyncthingService _syncthingService;
    private readonly ISyncSchedulerService _syncSchedulerService;
    private readonly WebDavStorageService _webDavService;
    private readonly OneDriveStorageService _oneDriveService;
    private readonly S3StorageService _s3Service;
    private readonly ILogger<AddTaskViewModel> _logger;

    public event EventHandler? TaskCreated;
    public event EventHandler<string>? ErrorOccurred;

    [ObservableProperty] private int _currentStep = 1;
    [ObservableProperty] private SyncTaskType _selectedType = SyncTaskType.Folder;
    [ObservableProperty] private string _taskName = string.Empty;
    [ObservableProperty] private string _localPath = string.Empty;
    [ObservableProperty] private SyncMode _selectedMode = SyncMode.RealTime;
    [ObservableProperty] private string _cronExpression = string.Empty;
    [ObservableProperty] private SyncDirection _selectedDirection = SyncDirection.Push;
    [ObservableProperty] private string _remotePath = "/";

    // Selected storage targets (multi-select)
    public ObservableCollection<StorageTarget> SelectedTargets { get; } = new();

    // Available servers loaded from settings
    public ObservableCollection<WebDavServerConfig> AvailableWebDav { get; } = new();
    public ObservableCollection<OneDriveServerConfig> AvailableOneDrive { get; } = new();
    public ObservableCollection<S3ServerConfig> AvailableS3 { get; } = new();

    public bool HasWebDav => AvailableWebDav.Count > 0;
    public bool HasOneDrive => AvailableOneDrive.Count > 0;
    public bool HasS3 => AvailableS3.Count > 0;
    public bool HasAnyCloud => HasWebDav || HasOneDrive || HasS3;

    public bool IsRealTimeMode
    {
        get => SelectedMode == SyncMode.RealTime;
        set { if (value) SelectedMode = SyncMode.RealTime; OnPropertyChanged(); OnPropertyChanged(nameof(IsScheduledMode)); }
    }

    public bool IsScheduledMode
    {
        get => SelectedMode == SyncMode.Scheduled;
        set { if (value) SelectedMode = SyncMode.Scheduled; OnPropertyChanged(); OnPropertyChanged(nameof(IsRealTimeMode)); }
    }

    public AddTaskViewModel(
        ITaskRepository taskRepository,
        IFileTrackerService fileTrackerService,
        ISyncthingService syncthingService,
        ISyncSchedulerService syncSchedulerService,
        WebDavStorageService webDavService,
        OneDriveStorageService oneDriveService,
        S3StorageService s3Service,
        ILogger<AddTaskViewModel> logger)
    {
        _taskRepository = taskRepository;
        _fileTrackerService = fileTrackerService;
        _syncthingService = syncthingService;
        _syncSchedulerService = syncSchedulerService;
        _webDavService = webDavService;
        _oneDriveService = oneDriveService;
        _s3Service = s3Service;
        _logger = logger;
    }

    public async Task LoadAvailableServersAsync()
    {
        try
        {
            var settings = await ReadSettingsAsync();
            if (settings == null)
            {
                _logger.LogWarning("Failed to read settings");
                return;
            }

            AvailableWebDav.Clear();
            if (settings.WebDavServers != null)
                foreach (var s in settings.WebDavServers) AvailableWebDav.Add(s);

            AvailableOneDrive.Clear();
            if (settings.OneDriveAccounts != null)
                foreach (var s in settings.OneDriveAccounts) AvailableOneDrive.Add(s);

            AvailableS3.Clear();
            if (settings.S3Servers != null)
                foreach (var s in settings.S3Servers) AvailableS3.Add(s);

            _logger.LogInformation("Loaded servers: {WebDav} WebDAV, {OneDrive} OneDrive, {S3} S3",
                AvailableWebDav.Count, AvailableOneDrive.Count, AvailableS3.Count);

            OnPropertyChanged(nameof(HasWebDav));
            OnPropertyChanged(nameof(HasOneDrive));
            OnPropertyChanged(nameof(HasS3));
            OnPropertyChanged(nameof(HasAnyCloud));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load available servers");
        }
    }

    [RelayCommand]
    public void NextStep()
    {
        if (CurrentStep < 4) CurrentStep++;
    }

    [RelayCommand]
    public void PrevStep()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    [RelayCommand]
    private void ToggleTarget(object? param)
    {
        if (param is not StorageTarget target) return;

        var existing = SelectedTargets.FirstOrDefault(t =>
            t.Type == target.Type && t.ServerId == target.ServerId);

        if (existing != null)
            SelectedTargets.Remove(existing);
        else
            SelectedTargets.Add(target);
    }

    public bool IsTargetSelected(StorageType type, Guid serverId)
    {
        return SelectedTargets.Any(t => t.Type == type && t.ServerId == serverId);
    }

    [RelayCommand]
    public async Task CreateTaskAsync()
    {
        try
        {
            SyncTask task;

            if (EditingTask != null)
            {
                // Update existing task
                task = EditingTask;
                task.Name = TaskName;
                task.Type = SelectedType;
                task.OriginalPath = LocalPath;
                task.CurrentPath = LocalPath;
                task.Mode = SelectedMode;
                task.CronExpression = SelectedMode == SyncMode.Scheduled ? CronExpression : null;
                task.Direction = SelectedDirection;
                task.RemotePath = RemotePath;
                task.StorageTargets = new List<StorageTarget>(SelectedTargets);
                foreach (var target in task.StorageTargets)
                    target.RemotePath = RemotePath;
                task.PathIsValid = File.Exists(LocalPath) || Directory.Exists(LocalPath);
                task.UpdatedAt = DateTime.Now;

                await _taskRepository.UpdateAsync(task);
                EditingTask = null;
            }
            else
            {
                // Create new task
                task = new SyncTask
                {
                    Name = TaskName,
                    Type = SelectedType,
                    OriginalPath = LocalPath,
                    CurrentPath = LocalPath,
                    Mode = SelectedMode,
                    CronExpression = SelectedMode == SyncMode.Scheduled ? CronExpression : null,
                    Direction = SelectedDirection,
                    RemotePath = RemotePath,
                    StorageTargets = new List<StorageTarget>(SelectedTargets),
                    PathIsValid = File.Exists(LocalPath) || Directory.Exists(LocalPath)
                };

                foreach (var target in task.StorageTargets)
                    target.RemotePath = RemotePath;

                if (task.Type == SyncTaskType.SingleFile && File.Exists(LocalPath))
                    task.Identity = FileIdentity.FromFile(LocalPath);

                await _taskRepository.AddAsync(task);
            }

            // Start file tracking for single files
            if (task.Type == SyncTaskType.SingleFile)
            {
                try { await _fileTrackerService.StartTrackingAsync(task); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to start file tracking"); }
            }

            // Schedule if needed
            if (task.Mode == SyncMode.Scheduled)
            {
                try { await _syncSchedulerService.ScheduleAsync(task); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to setup schedule"); }
            }

            // Initial upload to all selected targets
            if (task.PathIsValid && task.StorageTargets.Count > 0)
            {
                foreach (var target in task.StorageTargets)
                {
                    try
                    {
                        var config = await ResolveConfigAsync(target);
                        if (config == null) continue;

                        var service = GetCloudService(target.Type);
                        if (task.Type == SyncTaskType.Folder)
                            await service.UploadFolderAsync(config, task.CurrentPath, target.RemotePath);
                        else
                            await service.UploadFileAsync(config, task.CurrentPath, target.RemotePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Initial upload failed for target {Target}", target.ServerName);
                    }
                }
            }

            TaskCreated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create task");
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    private async Task<CloudStorageConfig?> ResolveConfigAsync(StorageTarget target)
    {
        var settings = await ReadSettingsAsync();
        if (settings == null) return null;

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
            StorageType.WebDAV => _webDavService,
            StorageType.OneDrive => _oneDriveService,
            StorageType.S3 => _s3Service,
            _ => throw new ArgumentException($"Unsupported storage type: {type}")
        };
    }

    private async Task<AppSettings?> ReadSettingsAsync()
    {
        var settingsPath = AppSettings.GetSettingsPath();
        if (!System.IO.File.Exists(settingsPath)) return null;

        var raw = await System.IO.File.ReadAllTextAsync(settingsPath);
        var json = raw.TrimStart();
        if (!json.StartsWith('{'))
        {
            var decrypted = SecureStorage.Decrypt(raw);
            if (decrypted.StartsWith('{'))
                json = decrypted;
        }

        return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
    }
}
