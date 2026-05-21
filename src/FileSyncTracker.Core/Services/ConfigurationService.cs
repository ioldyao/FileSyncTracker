using System.Text.Json;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Security;
using Microsoft.Extensions.Logging;

namespace FileSyncTracker.Core.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly WebDavStorageService _webDavService;
    private readonly OneDriveStorageService _oneDriveService;
    private readonly S3StorageService _s3Service;
    private AppSettings? _cached;
    private string? _cachedJson;

    public ConfigurationService(
        ILogger<ConfigurationService> logger,
        WebDavStorageService webDavService,
        OneDriveStorageService oneDriveService,
        S3StorageService s3Service)
    {
        _logger = logger;
        _webDavService = webDavService;
        _oneDriveService = oneDriveService;
        _s3Service = s3Service;
    }

    public async Task<AppSettings?> GetSettingsAsync()
    {
        var settingsPath = AppSettings.GetSettingsPath();
        if (!File.Exists(settingsPath)) return null;

        var raw = await File.ReadAllTextAsync(settingsPath);
        var json = raw.TrimStart();

        // Serve from cache if file hasn't changed
        if (_cached != null && raw == _cachedJson)
            return _cached;

        if (!json.StartsWith('{'))
        {
            var decrypted = SecureStorage.Decrypt(raw);
            if (decrypted.StartsWith('{'))
                json = decrypted;
        }

        _cached = JsonSerializer.Deserialize<AppSettings>(json);
        _cachedJson = raw;
        return _cached;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(settings, options);
        var encrypted = SecureStorage.Encrypt(json);

        var settingsPath = AppSettings.GetSettingsPath();
        var dir = Path.GetDirectoryName(settingsPath);
        if (dir != null) Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(settingsPath, encrypted);

        _cached = settings;
        _cachedJson = encrypted;

        _logger.LogInformation("Settings saved to {Path}", settingsPath);
    }

    public static string DecryptIfNeeded(string raw)
    {
        var json = raw.TrimStart();
        if (!json.StartsWith('{'))
        {
            var decrypted = SecureStorage.Decrypt(raw);
            if (decrypted.StartsWith('{'))
                json = decrypted;
        }
        return json;
    }

    public CloudStorageConfig? ResolveConfig(AppSettings settings, StorageTarget target)
    {
        return target.Type switch
        {
            StorageType.WebDAV => settings.WebDavServers
                .Where(s => s.Id == target.ServerId)
                .Select(s => new CloudStorageConfig
                {
                    StorageType = StorageType.WebDAV,
                    WebDavUrl = s.Url,
                    WebDavUsername = s.Username,
                    WebDavPassword = s.Password,
                    RemotePath = target.RemotePath
                }).FirstOrDefault(),

            StorageType.OneDrive => settings.OneDriveAccounts
                .Where(s => s.Id == target.ServerId)
                .Select(s => new CloudStorageConfig
                {
                    StorageType = StorageType.OneDrive,
                    OneDriveToken = s.AccessToken,
                    RemotePath = target.RemotePath
                }).FirstOrDefault(),

            StorageType.S3 => settings.S3Servers
                .Where(s => s.Id == target.ServerId)
                .Select(s => new CloudStorageConfig
                {
                    StorageType = StorageType.S3,
                    S3Endpoint = s.Endpoint,
                    S3Bucket = s.Bucket,
                    S3AccessKey = s.AccessKey,
                    S3SecretKey = s.SecretKey,
                    S3Region = s.Region,
                    S3UsePathStyle = s.UsePathStyle,
                    RemotePath = target.RemotePath
                }).FirstOrDefault(),

            _ => null
        };
    }

    public ICloudStorageService GetCloudService(StorageType type)
    {
        return type switch
        {
            StorageType.WebDAV => _webDavService,
            StorageType.OneDrive => _oneDriveService,
            StorageType.S3 => _s3Service,
            _ => throw new ArgumentException($"Unsupported storage type: {type}")
        };
    }
}
