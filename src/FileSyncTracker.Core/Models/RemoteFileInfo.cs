namespace FileSyncTracker.Core.Models;

public class RemoteFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
}
