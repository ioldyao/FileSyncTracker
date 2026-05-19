namespace FileSyncTracker.Core.Database;

public enum SyncDirection
{
    Upload,
    Download
}

public enum JournalStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

public class SyncJournal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public SyncDirection Direction { get; set; }
    public JournalStatus Status { get; set; } = JournalStatus.Pending;
    public string? LocalHash { get; set; }
    public string? RemoteHash { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
