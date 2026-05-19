namespace FileSyncTracker.Core.Models;

public enum StorageType
{
    Local,       // 本地 Syncthing 同步
    WebDAV,      // WebDAV 网盘
    OneDrive,    // Microsoft OneDrive
    S3           // S3 兼容存储 (AWS S3, 阿里云 OSS, MinIO 等)
}
