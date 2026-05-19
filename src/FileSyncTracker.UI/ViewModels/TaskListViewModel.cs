using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Security;
using FileSyncTracker.Core.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.ViewModels;

public partial class TaskListViewModel : ObservableObject
{
    private readonly ITaskRepository _taskRepository;
    private readonly ISyncthingService _syncthingService;
    private readonly IFileTrackerService _fileTrackerService;
    private readonly ISyncSchedulerService _syncSchedulerService;
    private readonly WebDavStorageService _webDavService;
    private readonly ILogger<TaskListViewModel> _logger;

    public ObservableCollection<SyncTask> Tasks { get; } = new();

    public TaskListViewModel(
        ITaskRepository taskRepository,
        ISyncthingService syncthingService,
        IFileTrackerService fileTrackerService,
        ISyncSchedulerService syncSchedulerService,
        WebDavStorageService webDavService,
        ILogger<TaskListViewModel> logger)
    {
        _taskRepository = taskRepository;
        _syncthingService = syncthingService;
        _fileTrackerService = fileTrackerService;
        _syncSchedulerService = syncSchedulerService;
        _webDavService = webDavService;
        _logger = logger;
        _ = LoadTasksAsync();
    }

    private async Task LoadTasksAsync()
    {
        var tasks = await _taskRepository.GetAllAsync();
        Tasks.Clear();
        foreach (var task in tasks)
            Tasks.Add(task);
    }

    public async Task RefreshAsync()
    {
        await LoadTasksAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync(SyncTask task)
    {
        if (task == null) return;

        task.Status = SyncStatus.Syncing;
        task.UpdatedAt = System.DateTime.Now;
        await _taskRepository.UpdateAsync(task);

        try
        {
            await _syncSchedulerService.TriggerNowAsync(task.Id);
            task.Status = SyncStatus.Idle;
            task.LastSyncTime = System.DateTime.Now;
        }
        catch (System.Exception ex)
        {
            task.Status = SyncStatus.Error;
            task.LastError = ex.Message;
        }

        await _taskRepository.UpdateAsync(task);
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(SyncTask task)
    {
        if (task == null) return;

        task.IsEnabled = !task.IsEnabled;
        task.Status = task.IsEnabled ? SyncStatus.Idle : SyncStatus.Disabled;
        task.UpdatedAt = System.DateTime.Now;
        await _taskRepository.UpdateAsync(task);
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(SyncTask task)
    {
        if (task == null) return;

        await _fileTrackerService.StopTrackingAsync(task.Id);
        await _syncSchedulerService.UnscheduleAsync(task.Id);
        await _taskRepository.DeleteAsync(task.Id);
        Tasks.Remove(task);
    }

    [RelayCommand]
    private void EditTask(SyncTask? task)
    {
        if (task == null) return;
        AddTaskViewModel.EditingTask = task;
        FileSyncTracker.UI.Views.MainWindow.Instance.ShowPage("AddTask");
    }

    [RelayCommand]
    private async Task DownloadAsync(SyncTask task)
    {
        if (task == null || task.StorageTargets == null || task.StorageTargets.Count == 0)
        {
            _logger.LogWarning("Download: No storage targets for task {TaskId}", task?.Id);
            return;
        }

        _logger.LogInformation("Download: Starting download for task {TaskId}", task.Id);

        // 如果没有设置下载路径，弹出文件选择框
        if (string.IsNullOrEmpty(task.DownloadPath))
        {
            _logger.LogInformation("Download: No download path set, showing file picker");
            var topLevel = TopLevel.GetTopLevel(FileSyncTracker.UI.Views.MainWindow.Instance);
            if (topLevel == null)
            {
                _logger.LogWarning("Download: Cannot get TopLevel");
                return;
            }

            var fileName = Path.GetFileName(task.CurrentPath);
            var files = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "选择下载保存位置",
                SuggestedFileName = fileName,
                DefaultExtension = Path.GetExtension(fileName)
            });

            if (files == null)
            {
                _logger.LogInformation("Download: User cancelled file picker");
                return;
            }

            task.DownloadPath = files.Path.LocalPath;
            _logger.LogInformation("Download: User selected path: {Path}", task.DownloadPath);
            await _taskRepository.UpdateAsync(task);
        }

        task.Status = SyncStatus.Syncing;
        await _taskRepository.UpdateAsync(task);

        try
        {
            var settings = await ReadSettingsAsync();
            if (settings == null)
            {
                _logger.LogWarning("Download: Cannot read settings");
                task.LastError = "无法读取设置";
                task.Status = SyncStatus.Error;
                await _taskRepository.UpdateAsync(task);
                return;
            }

            // Ensure download directory exists
            var downloadDir = Path.GetDirectoryName(task.DownloadPath);
            if (!string.IsNullOrEmpty(downloadDir) && !Directory.Exists(downloadDir))
                Directory.CreateDirectory(downloadDir);

            var fileName = Path.GetFileName(task.CurrentPath);
            _logger.LogInformation("Download: File name: {FileName}, Download path: {DownloadPath}", fileName, task.DownloadPath);

            foreach (var target in task.StorageTargets)
            {
                try
                {
                    _logger.LogInformation("Download: Trying target {Target} (Type={Type})", target.ServerName, target.Type);
                    var config = ResolveConfig(settings, target);
                    if (config == null)
                    {
                        _logger.LogWarning("Download: Cannot resolve config for {Target}", target.ServerName);
                        continue;
                    }

                    // DownloadFileAsync 会自动添加 RemotePath，所以只传文件名
                    _logger.LogInformation("Download: Downloading {FileName} from WebDAV", fileName);
                    await _webDavService.DownloadFileAsync(config, fileName, task.DownloadPath);

                    _logger.LogInformation("Download: Download complete, updating task");
                    // Update task path to download path
                    task.CurrentPath = task.DownloadPath;
                    task.PathIsValid = true;
                    task.Status = SyncStatus.Idle;
                    task.LastSyncTime = System.DateTime.Now;
                    await _taskRepository.UpdateAsync(task);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Download: Failed from target {Target}", target.ServerName);
                    task.LastError = ex.Message;
                }
            }

            _logger.LogWarning("Download: All targets failed");
            task.Status = SyncStatus.Error;
            await _taskRepository.UpdateAsync(task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download: Failed");
            task.Status = SyncStatus.Error;
            task.LastError = ex.Message;
            await _taskRepository.UpdateAsync(task);
        }
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
}
