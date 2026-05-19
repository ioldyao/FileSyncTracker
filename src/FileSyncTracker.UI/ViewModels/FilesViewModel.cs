using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Security;
using FileSyncTracker.Core.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace FileSyncTracker.UI.ViewModels;

public partial class FilesViewModel : ObservableObject
{
    private readonly ITaskRepository _taskRepository;

    public ObservableCollection<TaskGroupViewModel> TaskGroups { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }

    public FilesViewModel(ITaskRepository taskRepository)
    {
        _taskRepository = taskRepository;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        _ = LoadAsync();
    }

    public async Task RefreshAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        TaskGroups.Clear();

        var tasks = await _taskRepository.GetAllAsync();
        var settings = await ReadSettingsAsync();
        if (settings == null) return;

        // 按服务器分组: key = ServerId, value = (ServerName, ServerType)
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
                var config = ResolveConfig(settings, target);
                if (config == null) continue;

                try
                {
                    var service = GetCloudService(target.Type);
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
                            RemotePath = file
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
            catch { }
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

    private static ICloudStorageService GetCloudService(StorageType type)
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
