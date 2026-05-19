using FileSyncTracker.Core.Models;

namespace FileSyncTracker.Core.Services;

public interface ISyncSchedulerService
{
    Task ScheduleAsync(SyncTask task);
    Task UnscheduleAsync(Guid taskId);
    Task TriggerNowAsync(Guid taskId);
}
