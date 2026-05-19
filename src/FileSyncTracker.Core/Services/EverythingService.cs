using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using FileSyncTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace FileSyncTracker.Core.Services;

public class EverythingService : IEverythingService
{
    private readonly ILogger<EverythingService> _logger;

    public bool IsAvailable { get; private set; }
    public string? ErrorMessage { get; private set; }

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern uint Everything_SetSearchW(string lpSearchString);

    [DllImport("Everything64.dll")]
    private static extern bool Everything_QueryW(bool bWait);

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetNumResults();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern void Everything_GetResultFullPathNameW(uint nIndex, StringBuilder lpString, uint nMaxCount);

    [DllImport("Everything64.dll")]
    private static extern bool Everything_GetResultSize(uint nIndex, out long lpFileSize);

    [DllImport("Everything64.dll")]
    private static extern bool Everything_GetResultDateModified(uint nIndex, out long lpFileTime);

    [DllImport("Everything64.dll")]
    private static extern void Everything_Reset();

    public EverythingService(ILogger<EverythingService> logger)
    {
        _logger = logger;
        CheckAvailability();
    }

    private void CheckAvailability()
    {
        try
        {
            // 检查 Everything 进程是否运行
            var processes = Process.GetProcessesByName("Everything");
            if (processes.Length == 0)
            {
                IsAvailable = false;
                ErrorMessage = "Everything 未启动，请先启动 Everything";
                _logger.LogWarning("Everything process not found. Please start Everything.");
                return;
            }

            // 检查 DLL 是否存在，不存在则自动下载（后台异步）
            var appDir = AppContext.BaseDirectory;
            var dllPath = Path.Combine(appDir, "Everything64.dll");

            if (!File.Exists(dllPath))
            {
                _logger.LogInformation("Everything64.dll not found, starting background download...");
                // 后台下载，不阻塞主线程
                _ = Task.Run(async () =>
                {
                    var success = await DownloadEverythingSdkAsync(appDir);
                    if (success)
                    {
                        IsAvailable = true;
                        ErrorMessage = null;
                        _logger.LogInformation("Everything SDK downloaded and ready");
                    }
                    else
                    {
                        _logger.LogWarning("Everything SDK download failed in background");
                    }
                });

                IsAvailable = false;
                ErrorMessage = "Everything SDK 正在下载中...";
                return;
            }

            IsAvailable = true;
            ErrorMessage = null;
            _logger.LogInformation("Everything is available");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            ErrorMessage = $"检查 Everything 失败: {ex.Message}";
            _logger.LogError(ex, "Failed to check Everything availability");
        }
    }

    private async Task<bool> DownloadEverythingSdkAsync(string targetDir)
    {
        var sdkUrl = "https://www.voidtools.com/Everything-SDK.zip";
        var tempZip = Path.Combine(Path.GetTempPath(), "Everything-SDK.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), "Everything-SDK");

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            _logger.LogInformation("Downloading Everything SDK from {Url}", sdkUrl);
            var response = await httpClient.GetAsync(sdkUrl);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            _logger.LogInformation("Downloaded {Size} bytes", bytes.Length);

            await File.WriteAllBytesAsync(tempZip, bytes);

            _logger.LogInformation("Extracting SDK to {Dir}", extractDir);
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(tempZip, extractDir);

            // 查找 Everything64.dll
            var dllFile = Directory.GetFiles(extractDir, "Everything64.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dllFile == null)
            {
                _logger.LogWarning("Everything64.dll not found in downloaded SDK. Files: {Files}",
                    string.Join(", ", Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f))));
                return false;
            }

            // 复制到程序目录
            var destPath = Path.Combine(targetDir, "Everything64.dll");
            File.Copy(dllFile, destPath, overwrite: true);
            _logger.LogInformation("Copied Everything64.dll to {Path}", destPath);

            // 清理临时文件
            try { File.Delete(tempZip); } catch { }
            try { Directory.Delete(extractDir, true); } catch { }

            return true;
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Everything SDK download timed out");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download Everything SDK: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<string?> FindFileAsync(FileIdentity identity)
    {
        if (!IsAvailable)
        {
            CheckAvailability();
            if (!IsAvailable)
            {
                _logger.LogWarning("Everything not available: {Error}", ErrorMessage);
                return null;
            }
        }

        return await Task.Run(() =>
        {
            try
            {
                Everything_Reset();
                Everything_SetSearchW(identity.FileName);
                Everything_QueryW(true);

                var count = Everything_GetNumResults();
                _logger.LogInformation("Everything found {Count} results for {FileName}", count, identity.FileName);

                for (uint i = 0; i < count; i++)
                {
                    var sb = new StringBuilder(260);
                    Everything_GetResultFullPathNameW(i, sb, 260);
                    var fullPath = sb.ToString();

                    if (!File.Exists(fullPath)) continue;

                    var candidate = FileIdentity.FromFile(fullPath);

                    // 优先级 1：NTFS FileId 匹配
                    if (identity.NtfsFileId != 0 && candidate.NtfsFileId != 0
                        && identity.NtfsFileId == candidate.NtfsFileId)
                    {
                        _logger.LogInformation("File matched by NTFS FileId: {Path}", fullPath);
                        return fullPath;
                    }

                    // 优先级 2：完整身份匹配
                    if (identity.Matches(candidate))
                    {
                        _logger.LogInformation("File matched by full identity: {Path}", fullPath);
                        return fullPath;
                    }

                    // 优先级 3：文件名 + 大小匹配
                    if (identity.FallbackMatch(candidate))
                    {
                        _logger.LogInformation("File matched by fallback (name+size): {Path}", fullPath);
                        return fullPath;
                    }
                }

                _logger.LogWarning("Everything found {Count} results but no match for FileId={FileId}", count, identity.NtfsFileId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching with Everything");
                return null;
            }
        });
    }

    public async Task<List<string>> SearchAsync(string query, int maxResults = 10)
    {
        if (!IsAvailable) return new List<string>();

        return await Task.Run(() =>
        {
            try
            {
                Everything_Reset();
                Everything_SetSearchW(query);
                Everything_QueryW(true);

                var count = Math.Min(Everything_GetNumResults(), (uint)maxResults);
                var results = new List<string>();

                for (uint i = 0; i < count; i++)
                {
                    var sb = new StringBuilder(260);
                    Everything_GetResultFullPathNameW(i, sb, 260);
                    results.Add(sb.ToString());
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching with Everything: {Query}", query);
                return new List<string>();
            }
        });
    }
}
