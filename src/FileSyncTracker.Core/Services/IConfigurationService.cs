using FileSyncTracker.Core.Models;

namespace FileSyncTracker.Core.Services;

public interface IConfigurationService
{
    Task<AppSettings?> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    CloudStorageConfig? ResolveConfig(AppSettings settings, StorageTarget target);
    ICloudStorageService GetCloudService(StorageType type);
}
