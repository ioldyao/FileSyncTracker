namespace FileSyncTracker.Core.Models;

public class SyncTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public SyncTaskType Type { get; set; }

    // Path info
    public string OriginalPath { get; set; } = string.Empty;
    public string CurrentPath { get; set; } = string.Empty;
    public bool PathIsValid { get; set; } = true;

    // File identity (for SingleFile tracking)
    public FileIdentity? Identity { get; set; }

    // Sync config
    public string RemoteTarget { get; set; } = string.Empty;
    public SyncMode Mode { get; set; } = SyncMode.RealTime;
    public string? CronExpression { get; set; }
    public SyncDirection Direction { get; set; } = SyncDirection.Push;

    // Cloud storage targets (supports multi-server backup)
    public List<StorageTarget> StorageTargets { get; set; } = new();
    public string RemotePath { get; set; } = "/";

    // Status
    public SyncStatus Status { get; set; } = SyncStatus.Idle;
    public DateTime? LastSyncTime { get; set; }
    public string? LastError { get; set; }
    public bool IsEnabled { get; set; } = true;

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
