using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FileSyncTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace FileSyncTracker.Core.Services;

public class SyncthingService : ISyncthingService
{
    private readonly ILogger<SyncthingService> _logger;
    private readonly HttpClient _httpClient;
    private string _apiKey = string.Empty;
    private string _apiUrl = "http://127.0.0.1:8384";

    public SyncthingService(ILogger<SyncthingService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        LoadConfig();
    }

    private void LoadConfig()
    {
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Syncthing", "config.xml");

            if (File.Exists(configPath))
            {
                var doc = XDocument.Load(configPath);
                var apiKey = doc.Root?.Element("gui")?.Element("apikey")?.Value;
                if (!string.IsNullOrEmpty(apiKey))
                    _apiKey = apiKey;

                var address = doc.Root?.Element("gui")?.Element("address")?.Value;
                if (!string.IsNullOrEmpty(address))
                    _apiUrl = $"http://{address}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Syncthing config");
        }
    }

    public async Task<bool> IsRunningAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/rest/system/status");
            request.Headers.Add("X-API-Key", _apiKey);
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> GetApiKeyAsync() => Task.FromResult(_apiKey);

    public async Task AddFolderAsync(string localPath, string folderId, string remoteTarget)
    {
        try
        {
            var folderData = new
            {
                id = folderId,
                label = Path.GetFileName(localPath),
                path = localPath,
                type = "sendonly",
                fsWatcherEnabled = true,
                fsWatcherDelayS = 1
            };

            var request = new HttpRequestMessage(HttpMethod.Put, $"{_apiUrl}/rest/config/folders/{folderId}")
            {
                Content = JsonContent.Create(folderData)
            };
            request.Headers.Add("X-API-Key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Added Syncthing folder: {FolderId} -> {Path}", folderId, localPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add Syncthing folder: {FolderId}", folderId);
            throw;
        }
    }

    public async Task RemoveFolderAsync(string folderId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{_apiUrl}/rest/config/folders/{folderId}");
            request.Headers.Add("X-API-Key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Removed Syncthing folder: {FolderId}", folderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove Syncthing folder: {FolderId}", folderId);
            throw;
        }
    }

    public async Task TriggerSyncAsync(string folderId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiUrl}/rest/db/scan?folder={folderId}");
            request.Headers.Add("X-API-Key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Triggered sync for folder: {FolderId}", folderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger sync for folder: {FolderId}", folderId);
            throw;
        }
    }

    public async Task<SyncthingFolderStatus?> GetFolderStatusAsync(string folderId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/rest/db/status?folder={folderId}");
            request.Headers.Add("X-API-Key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new SyncthingFolderStatus
            {
                GlobalFiles = root.TryGetProperty("globalFiles", out var gf) ? gf.GetInt32() : 0,
                LocalFiles = root.TryGetProperty("localFiles", out var lf) ? lf.GetInt32() : 0,
                NeedFiles = root.TryGetProperty("needFiles", out var nf) ? nf.GetInt32() : 0,
                State = root.TryGetProperty("state", out var st) ? st.GetString() ?? "" : ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get folder status: {FolderId}", folderId);
            return null;
        }
    }
}
