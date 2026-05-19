using System.Net;
using System.Text;
using FileSyncTracker.Core.Models;
using Microsoft.Extensions.Logging;
using RemoteFileInfo = FileSyncTracker.Core.Models.RemoteFileInfo;

namespace FileSyncTracker.Core.Services;

public class WebDavStorageService : ICloudStorageService
{
    private readonly ILogger<WebDavStorageService> _logger;

    public WebDavStorageService(ILogger<WebDavStorageService> logger)
    {
        _logger = logger;
    }

    private HttpClient CreateClient(CloudStorageConfig config)
    {
        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(config.WebDavUsername, config.WebDavPassword)
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
    }

    private string NormalizeUrl(string baseUrl, string path)
    {
        baseUrl = baseUrl.TrimEnd('/');
        path = path.TrimStart('/');
        return $"{baseUrl}/{path}";
    }

    private string EncodePath(string path)
    {
        // URL-encode each path segment individually to preserve /
        var segments = path.Split('/');
        var encoded = string.Join("/", segments.Select(s => Uri.EscapeDataString(s)));
        return encoded;
    }

    public async Task<bool> TestConnectionAsync(CloudStorageConfig config)
    {
        try
        {
            using var client = CreateClient(config);
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, config.WebDavUrl));
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebDAV connection test failed");
            return false;
        }
    }

    public async Task UploadFileAsync(CloudStorageConfig config, string localPath, string remotePath, IProgress<UploadProgress>? progress = null)
    {
        _logger.LogInformation("WebDAV UploadFile: remotePath='{RemotePath}', config.RemotePath='{ConfigRemotePath}'", remotePath, config.RemotePath);

        using var client = CreateClient(config);
        var fileBytes = await File.ReadAllBytesAsync(localPath);

        var fileName = Path.GetFileName(localPath);
        var remoteFilePath = string.IsNullOrEmpty(remotePath) ? fileName : $"{remotePath.TrimStart('/')}/{fileName}";
        var encodedPath = EncodePath(remoteFilePath);
        var url = NormalizeUrl(config.WebDavUrl, encodedPath);

        _logger.LogInformation("WebDAV UploadFile: URL={Url}, FileSize={Size}", url, fileBytes.Length);

        // Ensure parent directory exists
        var dirPath = string.IsNullOrEmpty(remotePath) ? "" : remotePath.TrimStart('/');
        if (!string.IsNullOrEmpty(dirPath))
        {
            var encodedDirPath = EncodePath(dirPath);
            var dirUrl = NormalizeUrl(config.WebDavUrl, encodedDirPath);
            _logger.LogInformation("WebDAV EnsureDirectory: DirUrl={DirUrl}", dirUrl);
            await EnsureDirectoryExists(client, dirUrl);
        }

        using var content = new ByteArrayContent(fileBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = fileBytes.Length;

        // 直接 PUT 覆盖（带 Overwrite 头）
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = content
        };
        request.Headers.Add("Overwrite", "T");

        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("WebDAV PUT failed: {Status} {Body}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        progress?.Report(new UploadProgress
        {
            FileName = fileName,
            BytesUploaded = fileBytes.Length,
            TotalBytes = fileBytes.Length
        });

        _logger.LogInformation("Uploaded {File} to WebDAV: {Url}", localPath, url);
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
            var remoteFilePath = string.IsNullOrEmpty(remotePath) ? relativePath : $"{remotePath.TrimStart('/')}/{relativePath}";
            var encodedPath = EncodePath(remoteFilePath);

            using var client = CreateClient(config);
            var fileBytes = await File.ReadAllBytesAsync(file, ct);
            var url = NormalizeUrl(config.WebDavUrl, encodedPath);

            var dirPath = Path.GetDirectoryName(remoteFilePath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dirPath))
            {
                var encodedDirPath = EncodePath(dirPath);
                var dirUrl = NormalizeUrl(config.WebDavUrl, encodedDirPath);
                await EnsureDirectoryExists(client, dirUrl);
            }

            // Try DELETE first if file exists
            try
            {
                var headResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                if (headResponse.IsSuccessStatusCode)
                    await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));
            }
            catch { }

            using var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentLength = fileBytes.Length;

            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = content
            };
            request.Headers.Add("Overwrite", "T");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("WebDAV PUT failed for {File}: {Status} {Body}", relativePath, response.StatusCode, body);
                response.EnsureSuccessStatusCode();
            }

            uploadedBytes += fileBytes.Length;
            progress?.Report(new UploadProgress
            {
                FileName = relativePath,
                BytesUploaded = uploadedBytes,
                TotalBytes = totalBytes
            });
        }

        _logger.LogInformation("Uploaded folder {Folder} to WebDAV", localFolderPath);
    }

    public async Task DownloadFileAsync(CloudStorageConfig config, string remotePath, string localPath, IProgress<UploadProgress>? progress = null)
    {
        using var client = CreateClient(config);
        var remoteFilePath = string.IsNullOrEmpty(config.RemotePath) ? remotePath : $"{config.RemotePath.TrimStart('/')}/{remotePath}";
        var encodedPath = EncodePath(remoteFilePath);
        var url = NormalizeUrl(config.WebDavUrl, encodedPath);

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
        var remoteFilePath = string.IsNullOrEmpty(config.RemotePath) ? remotePath : $"{config.RemotePath.TrimStart('/')}/{remotePath}";
        var encodedPath = EncodePath(remoteFilePath);
        var url = NormalizeUrl(config.WebDavUrl, encodedPath);

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
        request.Headers.Add("Depth", "1");
        request.Content = new StringContent(
            "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>",
            Encoding.UTF8, "application/xml");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var files = new List<string>();

        var startTag = "<d:href>";
        var endTag = "</d:href>";
        int idx = 0;
        while ((idx = body.IndexOf(startTag, idx)) != -1)
        {
            idx += startTag.Length;
            var endIdx = body.IndexOf(endTag, idx);
            if (endIdx == -1) break;
            var href = body.Substring(idx, endIdx - idx);
            if (!href.EndsWith("/"))
                files.Add(WebUtility.UrlDecode(href));
            idx = endIdx + endTag.Length;
        }

        return files;
    }

    public async Task DeleteFileAsync(CloudStorageConfig config, string remotePath)
    {
        using var client = CreateClient(config);
        var remoteFilePath = string.IsNullOrEmpty(config.RemotePath) ? remotePath : $"{config.RemotePath.TrimStart('/')}/{remotePath}";
        var encodedPath = EncodePath(remoteFilePath);
        var url = NormalizeUrl(config.WebDavUrl, encodedPath);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> FileExistsAsync(CloudStorageConfig config, string remotePath)
    {
        try
        {
            using var client = CreateClient(config);
            var remoteFilePath = string.IsNullOrEmpty(config.RemotePath) ? remotePath : $"{config.RemotePath.TrimStart('/')}/{remotePath}";
            var encodedPath = EncodePath(remoteFilePath);
            var url = NormalizeUrl(config.WebDavUrl, encodedPath);
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取远端文件信息（大小、修改时间）
    /// </summary>
    public async Task<RemoteFileInfo?> GetFileInfoAsync(CloudStorageConfig config, string remotePath)
    {
        try
        {
            using var client = CreateClient(config);
            var remoteFilePath = string.IsNullOrEmpty(config.RemotePath) ? remotePath : $"{config.RemotePath.TrimStart('/')}/{remotePath}";
            var encodedPath = EncodePath(remoteFilePath);
            var url = NormalizeUrl(config.WebDavUrl, encodedPath);

            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            request.Headers.Add("Depth", "0");
            request.Content = new StringContent(
                "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>",
                Encoding.UTF8, "application/xml");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();

            // 解析文件大小
            long fileSize = 0;
            var sizeTag = "<d:getcontentlength>";
            var sizeEndTag = "</d:getcontentlength>";
            var sizeIdx = body.IndexOf(sizeTag);
            if (sizeIdx != -1)
            {
                sizeIdx += sizeTag.Length;
                var sizeEndIdx = body.IndexOf(sizeEndTag, sizeIdx);
                if (sizeEndIdx != -1)
                    long.TryParse(body.Substring(sizeIdx, sizeEndIdx - sizeIdx), out fileSize);
            }

            // 解析修改时间
            DateTime lastModified = DateTime.MinValue;
            var timeTag = "<d:getlastmodified>";
            var timeEndTag = "</d:getlastmodified>";
            var timeIdx = body.IndexOf(timeTag);
            if (timeIdx != -1)
            {
                timeIdx += timeTag.Length;
                var timeEndIdx = body.IndexOf(timeEndTag, timeIdx);
                if (timeEndIdx != -1)
                {
                    var timeStr = body.Substring(timeIdx, timeEndIdx - timeIdx);
                    DateTime.TryParse(timeStr, out lastModified);
                }
            }

            return new RemoteFileInfo
            {
                FileName = Path.GetFileName(remotePath),
                FileSize = fileSize,
                LastModified = lastModified
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureDirectoryExists(HttpClient client, string dirUrl)
    {
        try
        {
            var request = new HttpRequestMessage(new HttpMethod("MKCOL"), dirUrl);
            request.Headers.Add("Overwrite", "T");
            var response = await client.SendAsync(request);
            _logger.LogInformation("MKCOL {Url}: {Status}", dirUrl, response.StatusCode);
            // 201 Created = success, 405 Method Not Allowed = already exists, 409 Conflict = parent missing or exists
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MKCOL failed for {Url}", dirUrl);
        }
    }
}
