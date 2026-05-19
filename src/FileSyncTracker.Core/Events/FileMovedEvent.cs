namespace FileSyncTracker.Core.Events;

public class FileMovedEvent
{
    public Guid TaskId { get; set; }
    public string OldPath { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
}
