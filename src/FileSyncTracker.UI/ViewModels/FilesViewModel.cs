using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Services;
using FileSyncTracker.UI.Views;
using System.Collections.ObjectModel;

namespace FileSyncTracker.UI.ViewModels;

public partial class FilesViewModel : ObservableObject
{
    private readonly IConfigurationService _configService;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ObservableCollection<TaskGroupViewModel> TaskGroups { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }

    public FilesViewModel(IConfigurationService configService)
    {
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

            var settings = await _configService.GetSettingsAsync();
            if (settings == null) return;

            // WebDAV servers
            foreach (var server in settings.WebDavServers)
            {
                var group = new TaskGroupViewModel
                {
                    ServerName = server.Name,
                    ServerType = nameof(StorageType.WebDAV)
                };

                var config = new CloudStorageConfig
                {
                    StorageType = StorageType.WebDAV,
                    WebDavUrl = server.Url,
                    WebDavUsername = server.Username,
                    WebDavPassword = server.Password,
                    RemotePath = server.RemotePath
                };

                try
                {
                    var service = _configService.GetCloudService(StorageType.WebDAV);
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
                            RemotePath = file,
                            ServerType = StorageType.WebDAV,
                            Config = config
                        });
                    }
                }
                catch (Exception ex)
                {
                    group.Files.Add(new RemoteFileItem { FileName = $"[加载失败: {ex.Message}]" });
                }

                TaskGroups.Add(group);
            }

            // OneDrive accounts
            foreach (var account in settings.OneDriveAccounts)
            {
                var group = new TaskGroupViewModel
                {
                    ServerName = account.Name,
                    ServerType = nameof(StorageType.OneDrive)
                };

                var config = new CloudStorageConfig
                {
                    StorageType = StorageType.OneDrive,
                    OneDriveToken = account.AccessToken,
                    RemotePath = account.RemotePath
                };

                try
                {
                    var service = _configService.GetCloudService(StorageType.OneDrive);
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
                            RemotePath = file,
                            ServerType = StorageType.OneDrive,
                            Config = config
                        });
                    }
                }
                catch (Exception ex)
                {
                    group.Files.Add(new RemoteFileItem { FileName = $"[加载失败: {ex.Message}]" });
                }

                TaskGroups.Add(group);
            }

            // S3 servers
            foreach (var server in settings.S3Servers)
            {
                var group = new TaskGroupViewModel
                {
                    ServerName = server.Name,
                    ServerType = nameof(StorageType.S3)
                };

                var config = new CloudStorageConfig
                {
                    StorageType = StorageType.S3,
                    S3Endpoint = server.Endpoint,
                    S3Bucket = server.Bucket,
                    S3AccessKey = server.AccessKey,
                    S3SecretKey = server.SecretKey,
                    S3Region = server.Region,
                    S3UsePathStyle = server.UsePathStyle,
                    RemotePath = server.RemotePath
                };

                try
                {
                    var service = _configService.GetCloudService(StorageType.S3);
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
                            RemotePath = file,
                            ServerType = StorageType.S3,
                            Config = config
                        });
                    }
                }
                catch (Exception ex)
                {
                    group.Files.Add(new RemoteFileItem { FileName = $"[加载失败: {ex.Message}]" });
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
