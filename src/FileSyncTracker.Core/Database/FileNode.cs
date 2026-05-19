using System.ComponentModel.DataAnnotations;

namespace FileSyncTracker.Core.Database;

public enum SyncNodeStatus
{
    Synced,
    Pending,
    Conflict,
    Deleted
}

public class FileNode
{
    public Guid TaskId { get; set; }
    public string RelativePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    // Local state
    public long LocalSize { get; set; }
    public DateTime LocalMTimeUtc { get; set; }
    public string? LocalContentHash { get; set; }

    // Remote state
    public long RemoteSize { get; set; }
    public DateTime RemoteMTimeUtc { get; set; }
    public string? RemoteETag { get; set; }

    public SyncNodeStatus SyncStatus { get; set; } = SyncNodeStatus.Pending;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
