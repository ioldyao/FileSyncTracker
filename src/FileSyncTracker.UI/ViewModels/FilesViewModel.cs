using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Services;
using FileSyncTracker.UI.Views;
using System.Collections.ObjectModel;

namespace FileSyncTracker.UI.ViewModels;

public partial class FilesViewModel : ObservableObject
{
    private readonly ITaskRepository _taskRepository;
    private readonly IConfigurationService _configService;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ObservableCollection<TaskGroupViewModel> TaskGroups { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }

    public FilesViewModel(ITaskRepository taskRepository, IConfigurationService configService)
    {
        _taskRepository = taskRepository;
        _configService = configService;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public async Task RefreshAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        if (!await _lock.WaitAsync(0)) return;
        try
        {
            TaskGroups.Clear();

            var tasks = await _taskRepository.GetAllAsync();
            var settings = await _configService.GetSettingsAsync();
            if (settings == null) return;

            var serverMap = new Dictionary<Guid, (string Name, StorageType Type, List<SyncTask> Tasks)>();

            foreach (var task in tasks.Where(t => t.StorageTargets is { Count: > 0 }))
            {
                foreach (var target in task.StorageTargets!)
                {
                    if (!serverMap.TryGetValue(target.ServerId, out var entry))
                    {
                        entry = (target.ServerName, target.Type, new List<SyncTask>());
                        serverMap[target.ServerId] = entry;
                    }
                    entry.Tasks.Add(task);
                }
            }

            foreach (var (serverId, (serverName, serverType, serverTasks)) in serverMap)
            {
                var group = new TaskGroupViewModel
                {
                    ServerName = serverName,
                    ServerType = serverType.ToString()
                };

                foreach (var task in serverTasks)
                {
                    var target = task.StorageTargets!.First(t => t.ServerId == serverId);
                    var config = _configService.ResolveConfig(settings, target);
                    if (config == null) continue;

                    try
                    {
                        var service = _configService.GetCloudService(target.Type);
                        var files = await service.ListFilesAsync(config, "");

                        foreach (var file in files)
                        {
                            var fileName = file.TrimEnd('/').Split('/').LastOrDefault() ?? file;
                            var fileInfo = await service.GetFileInfoAsync(config, fileName);

                            group.Files.Add(new RemoteFileItem
                        {
                            FileName = fileName,
                            FileSize = fileInfo?.FileSize ?? 0,
                            LastModified = fileInfo?.LastModified ?? DateTime.MinValue,
                            TaskName = task.Name,
                            RemotePath = file,
                            ServerType = target.Type,
                            Config = config
                        });
                    }
                }
                catch (Exception ex)
                {
                    group.Files.Add(new RemoteFileItem
                    {
                        FileName = $"[加载失败: {ex.Message}]",
                        TaskName = task.Name
                    });
                }
            }

            TaskGroups.Add(group);
        }
        }
        finally
        {
            _lock.Release();
        }
    }

    [RelayCommand]
    private async Task DownloadFileAsync(RemoteFileItem? item)
    {
        if (item?.Config == null) return;

        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择下载保存位置",
            SuggestedFileName = item.FileName,
            DefaultExtension = Path.GetExtension(item.FileName)
        });

        if (file == null) return;

        try
        {
            var service = _configService.GetCloudService(item.ServerType);
            await service.DownloadFileAsync(item.Config, item.RemotePath, file.Path.LocalPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Download] Failed: {ex.Message}");
        }
    }

}

public class TaskGroupViewModel
{
    public string ServerName { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public ObservableCollection<RemoteFileItem> Files { get; } = new();
}

public class RemoteFileItem
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public StorageType ServerType { get; set; }
    public CloudStorageConfig? Config { get; set; }

    public string FileSizeDisplay => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{FileSize / (1024.0 * 1024):F1} MB",
        _ => $"{FileSize / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string LastModifiedDisplay => LastModified == DateTime.MinValue
        ? "-"
        : LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
