using FileSyncTracker.Core.Models;

namespace FileSyncTracker.Core.Events;

public class SyncStatusChangedEvent
{
    public Guid TaskId { get; set; }
    public SyncStatus NewStatus { get; set; }
    public string? ErrorMessage { get; set; }
}
