using FileSyncTracker.Core.Models;

namespace FileSyncTracker.Core.Services;

public interface IFileTrackerService
{
    Task StartTrackingAsync(SyncTask task);
    Task StopTrackingAsync(Guid taskId);
    Task<bool> TryResolvePathAsync(SyncTask task);
}
