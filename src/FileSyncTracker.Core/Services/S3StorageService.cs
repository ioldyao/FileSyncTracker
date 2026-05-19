using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using FileSyncTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace FileSyncTracker.Core.Services;

public class S3StorageService : ICloudStorageService
{
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(ILogger<S3StorageService> logger)
    {
        _logger = logger;
    }

    private IAmazonS3 CreateClient(CloudStorageConfig config)
    {
        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = !string.IsNullOrEmpty(config.S3Region)
                ? RegionEndpoint.GetBySystemName(config.S3Region)
                : RegionEndpoint.USEast1,
            ForcePathStyle = config.S3UsePathStyle,
            UseHttp = true
        };

        if (!string.IsNullOrEmpty(config.S3Endpoint))
            s3Config.ServiceURL = config.S3Endpoint;

        return new AmazonS3Client(config.S3AccessKey, config.S3SecretKey, s3Config);
    }

    private string GetRemoteKey(CloudStorageConfig config, string remotePath)
    {
        var prefix = config.RemotePath.Trim('/');
        var path = remotePath.TrimStart('/');
        return string.IsNullOrEmpty(prefix) ? path : $"{prefix}/{path}";
    }

    public async Task<bool> TestConnectionAsync(CloudStorageConfig config)
    {
        try
        {
            using var client = CreateClient(config);
            await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = config.S3Bucket,
                MaxKeys = 1
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3 connection test failed");
            return false;
        }
    }

    public async Task UploadFileAsync(CloudStorageConfig config, string localPath, string remotePath, IProgress<UploadProgress>? progress = null)
    {
        using var client = CreateClient(config);
        var key = GetRemoteKey(config, remotePath);
        var fileBytes = await File.ReadAllBytesAsync(localPath);

        var request = new PutObjectRequest
        {
            BucketName = config.S3Bucket,
            Key = key,
            InputStream = new MemoryStream(fileBytes)
        };

        await client.PutObjectAsync(request);

        progress?.Report(new UploadProgress
        {
            FileName = Path.GetFileName(localPath),
            BytesUploaded = fileBytes.Length,
            TotalBytes = fileBytes.Length
        });

        _logger.LogInformation("Uploaded {File} to S3: s3://{Bucket}/{Key}", localPath, config.S3Bucket, key);
    }

    public async Task UploadFolderAsync(CloudStorageConfig config, string localFolderPath, string remotePath, IProgress<UploadProgress>? progress = null, CancellationToken ct = default)
    {
        var files = Directory.GetFiles(localFolderPath, "*", SearchOption.AllDirectories);
        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        long uploadedBytes = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(localFolderPath, file).Replace("\\", "/");
            var remoteFilePath = $"{remotePath.TrimStart('/')}/{relativePath}";

            using var client = CreateClient(config);
            var key = GetRemoteKey(config, remoteFilePath);
            var fileBytes = await File.ReadAllBytesAsync(file, ct);

            var request = new PutObjectRequest
            {
                BucketName = config.S3Bucket,
                Key = key,
                InputStream = new MemoryStream(fileBytes)
            };

            await client.PutObjectAsync(request, ct);

            uploadedBytes += fileBytes.Length;
            progress?.Report(new UploadProgress
            {
                FileName = relativePath,
                BytesUploaded = uploadedBytes,
                TotalBytes = totalBytes
            });
        }

        _logger.LogInformation("Uploaded folder {Folder} to S3: s3://{Bucket}/{Path}", localFolderPath, config.S3Bucket, config.RemotePath);
    }

    public async Task DownloadFileAsync(CloudStorageConfig config, string remotePath, string localPath, IProgress<UploadProgress>? progress = null)
    {
        using var client = CreateClient(config);
        var key = GetRemoteKey(config, remotePath);

        var response = await client.GetObjectAsync(config.S3Bucket, key);
        var dir = Path.GetDirectoryName(localPath);
        if (dir != null) Directory.CreateDirectory(dir);

        await using var fs = File.Create(localPath);
        await response.ResponseStream.CopyToAsync(fs);

        progress?.Report(new UploadProgress
        {
            FileName = Path.GetFileName(remotePath),
            BytesUploaded = response.ContentLength,
            TotalBytes = response.ContentLength
        });
    }

    public async Task<List<string>> ListFilesAsync(CloudStorageConfig config, string remotePath)
    {
        using var client = CreateClient(config);
        var prefix = GetRemoteKey(config, remotePath);
        if (!prefix.EndsWith("/")) prefix += "/";

        var files = new List<string>();
        string? continuationToken = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = config.S3Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            };

            var response = await client.ListObjectsV2Async(request);
            files.AddRange(response.S3Objects.Select(o => o.Key));
            continuationToken = response.NextContinuationToken;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return files;
    }

    public async Task DeleteFileAsync(CloudStorageConfig config, string remotePath)
    {
        using var client = CreateClient(config);
        var key = GetRemoteKey(config, remotePath);
        await client.DeleteObjectAsync(config.S3Bucket, key);
    }

    public async Task<bool> FileExistsAsync(CloudStorageConfig config, string remotePath)
    {
        try
        {
            using var client = CreateClient(config);
            var key = GetRemoteKey(config, remotePath);
            await client.GetObjectMetadataAsync(config.S3Bucket, key);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
