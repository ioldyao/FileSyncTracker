using FileSyncTracker.Core.Models;
using RemoteFileInfo = FileSyncTracker.Core.Models.RemoteFileInfo;

namespace FileSyncTracker.Core.Services;

public class UploadProgress
{
    public string FileName { get; set; } = string.Empty;
    public long BytesUploaded { get; set; }
    public long TotalBytes { get; set; }
    public double PercentComplete => TotalBytes > 0 ? (double)BytesUploaded / TotalBytes * 100 : 0;
}

public interface ICloudStorageService
{
    Task<bool> TestConnectionAsync(CloudStorageConfig config);
    Task UploadFileAsync(CloudStorageConfig config, string localPath, string remotePath, IProgress<UploadProgress>? progress = null);
    Task UploadFolderAsync(CloudStorageConfig config, string localFolderPath, string remotePath, IProgress<UploadProgress>? progress = null, CancellationToken ct = default);
    Task DownloadFileAsync(CloudStorageConfig config, string remotePath, string localPath, IProgress<UploadProgress>? progress = null);
    Task<List<string>> ListFilesAsync(CloudStorageConfig config, string remotePath);
    Task DeleteFileAsync(CloudStorageConfig config, string remotePath);
    Task<bool> FileExistsAsync(CloudStorageConfig config, string remotePath);
    Task<RemoteFileInfo?> GetFileInfoAsync(CloudStorageConfig config, string remotePath);
}
