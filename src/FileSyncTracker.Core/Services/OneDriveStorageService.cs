using System.Net.Http.Json;
using System.Text.Json;
using FileSyncTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace FileSyncTracker.Core.Services;

public class OneDriveStorageService : ICloudStorageService
{
    private readonly ILogger<OneDriveStorageService> _logger;
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    public OneDriveStorageService(ILogger<OneDriveStorageService> logger)
    {
        _logger = logger;
    }

    private HttpClient CreateClient(CloudStorageConfig config)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.OneDriveToken);
        return client;
    }

    private string GetItemPath(CloudStorageConfig config, string remotePath)
    {
        var prefix = config.RemotePath.Trim('/');
        var path = remotePath.TrimStart('/');
        var itemPath = string.IsNullOrEmpty(prefix) ? path : $"{prefix}/{path}";
        return Uri.EscapeDataString(itemPath);
    }

    public async Task<bool> TestConnectionAsync(CloudStorageConfig config)
    {
        try
        {
            using var client = CreateClient(config);
            var response = await client.GetAsync($"{GraphBaseUrl}/me/drive");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OneDrive connection test failed");
            return false;
        }
    }

    public async Task UploadFileAsync(CloudStorageConfig config, string localPath, string remotePath, IProgress<UploadProgress>? progress = null)
    {
        using var client = CreateClient(config);
        var fileBytes = await File.ReadAllBytesAsync(localPath);
        var itemPath = GetItemPath(config, remotePath);

        // For files < 4MB, use small file upload
        if (fileBytes.Length < 4 * 1024 * 1024)
        {
            var url = $"{GraphBaseUrl}/me/drive/root:/{itemPath}:/content";
            using var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var response = await client.PutAsync(url, content);
            response.EnsureSuccessStatusCode();
        }
        else
        {
            // Upload session for large files
            var sessionUrl = $"{GraphBaseUrl}/me/drive/root:/{itemPath}:/createUploadSession";
            var sessionResponse = await client.PostAsJsonAsync(sessionUrl, new
            {
                item = new { name = Path.GetFileName(localPath) }
            });
            sessionResponse.EnsureSuccessStatusCode();
            var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
            var uploadUrl = session.GetProperty("uploadUrl").GetString()!;

            var chunkSize = 10 * 1024 * 1024; // 10MB chunks
            for (int offset = 0; offset < fileBytes.Length; offset += chunkSize)
            {
                var end = Math.Min(offset + chunkSize, fileBytes.Length);
                var chunk = fileBytes[offset..end];

                using var chunkContent = new ByteArrayContent(chunk);
                chunkContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                chunkContent.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, end - 1, fileBytes.Length);

                var chunkResponse = await client.PutAsync(uploadUrl, chunkContent);
                chunkResponse.EnsureSuccessStatusCode();

                progress?.Report(new UploadProgress
                {
                    FileName = Path.GetFileName(localPath),
                    BytesUploaded = end,
                    TotalBytes = fileBytes.Length
                });
            }
        }

        progress?.Report(new UploadProgress
        {
            FileName = Path.GetFileName(localPath),
            BytesUploaded = fileBytes.Length,
            TotalBytes = fileBytes.Length
        });

        _logger.LogInformation("Uploaded {File} to OneDrive", localPath);
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
            var fileBytes = await File.ReadAllBytesAsync(file, ct);
            var itemPath = GetItemPath(config, remoteFilePath);

            var url = $"{GraphBaseUrl}/me/drive/root:/{itemPath}:/content";
            using var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var response = await client.PutAsync(url, content, ct);
            response.EnsureSuccessStatusCode();

            uploadedBytes += fileBytes.Length;
            progress?.Report(new UploadProgress
            {
                FileName = relativePath,
                BytesUploaded = uploadedBytes,
                TotalBytes = totalBytes
            });
        }

        _logger.LogInformation("Uploaded folder {Folder} to OneDrive", localFolderPath);
    }

    public async Task DownloadFileAsync(CloudStorageConfig config, string remotePath, string localPath, IProgress<UploadProgress>? progress = null)
    {
        using var client = CreateClient(config);
        var itemPath = GetItemPath(config, remotePath);
        var url = $"{GraphBaseUrl}/me/drive/root:/{itemPath}:/content";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var dir = Path.GetDirectoryName(localPath);
        if (dir != null) Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(localPath, bytes);

        progress?.Report(new UploadProgress
        {
            FileName = Path.GetFileName(remotePath),
            BytesUploaded = bytes.Length,
            TotalBytes = bytes.Length
        });
    }

    public async Task<List<string>> ListFilesAsync(CloudStorageConfig config, string remotePath)
    {
        using var client = CreateClient(config);
        var itemPath = GetItemPath(config, remotePath);
        var url = $"{GraphBaseUrl}/me/drive/root:/{itemPath}:/children";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var files = new List<string>();

        if (json.TryGetProperty("value", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name))
                    files.Add(name.GetString() ?? "");
            }
        }

        return files;
    }

    public async Task DeleteFileAsync(CloudStorageConfig config, string remotePath)
    {
        using var client = CreateClient(config);
        var itemPath = GetItemPath(config, remotePath);
        var url = $"{GraphBaseUrl}/me/drive/root:/{itemPath}";
        var response = await client.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> FileExistsAsync(CloudStorageConfig config, string remotePath)
    {
        try
        {
            using var client = CreateClient(config);
            var itemPath = GetItemPath(config, remotePath);
            var url = $"{GraphBaseUrl}/me/drive/root:/{itemPath}";
            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
