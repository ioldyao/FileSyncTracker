namespace FileSyncTracker.Core.Models;

/// <summary>
/// A single storage target for a sync task.
/// References a pre-configured server by Id.
/// </summary>
public class StorageTarget
{
    public StorageType Type { get; set; } = StorageType.Local;
    public Guid ServerId { get; set; }            // Reference to WebDavServerConfig/OneDriveServerConfig/S3ServerConfig
    public string ServerName { get; set; } = string.Empty;  // Display name (denormalized for quick display)
    public string RemotePath { get; set; } = "/"; // Path on remote storage
}
