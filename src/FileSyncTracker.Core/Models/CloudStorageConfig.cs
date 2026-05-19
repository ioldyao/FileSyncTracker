namespace FileSyncTracker.Core.Models;

public class CloudStorageConfig
{
    public StorageType StorageType { get; set; } = StorageType.Local;

    // WebDAV
    public string WebDavUrl { get; set; } = string.Empty;
    public string WebDavUsername { get; set; } = string.Empty;
    public string WebDavPassword { get; set; } = string.Empty;

    // OneDrive
    public string OneDriveToken { get; set; } = string.Empty;
    public string OneDriveRefreshToken { get; set; } = string.Empty;

    // S3
    public string S3Endpoint { get; set; } = string.Empty;
    public string S3Bucket { get; set; } = string.Empty;
    public string S3AccessKey { get; set; } = string.Empty;
    public string S3SecretKey { get; set; } = string.Empty;
    public string S3Region { get; set; } = string.Empty;
    public bool S3UsePathStyle { get; set; } = true;

    // Remote path prefix on storage
    public string RemotePath { get; set; } = "/";
}
