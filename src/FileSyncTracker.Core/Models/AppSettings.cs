namespace FileSyncTracker.Core.Models;

public class AppSettings
{
    public string EverythingPath { get; set; } = @"C:\Program Files\Everything\Everything64.dll";
    public string SyncthingPath { get; set; } = @"C:\Program Files\Syncthing\syncthing.exe";
    public string SyncthingApiKey { get; set; } = string.Empty;
    public string SyncthingApiUrl { get; set; } = "http://127.0.0.1:8384";
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStart { get; set; }
    public int LogRetentionDays { get; set; } = 30;

    // Cloud storage servers
    public List<WebDavServerConfig> WebDavServers { get; set; } = new();
    public List<OneDriveServerConfig> OneDriveAccounts { get; set; } = new();
    public List<S3ServerConfig> S3Servers { get; set; } = new();

    public static string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "FileSyncTracker", "settings.json");
    }
}

public class WebDavServerConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;       // e.g. "坚果云", "NextCloud"
    public string Url { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class OneDriveServerConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;       // e.g. "个人OneDrive", "工作OneDrive"
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public class S3ServerConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;       // e.g. "阿里云OSS", "MinIO"
    public string Endpoint { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool UsePathStyle { get; set; } = true;
}
