using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    // General settings
    [ObservableProperty] private string _everythingPath = @"C:\Program Files\Everything\Everything64.dll";
    [ObservableProperty] private string _syncthingPath = @"C:\Program Files\Syncthing\syncthing.exe";
    [ObservableProperty] private string _syncthingApiUrl = "http://127.0.0.1:8384";
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private int _logRetentionDays = 30;
    [ObservableProperty] private string _everythingStatusText = "Checking...";
    [ObservableProperty] private string _syncthingStatusText = "Checking...";

    // WebDAV servers
    public ObservableCollection<WebDavServerConfig> WebDavServers { get; } = new();

    // OneDrive accounts
    public ObservableCollection<OneDriveServerConfig> OneDriveAccounts { get; } = new();

    // S3 servers
    public ObservableCollection<S3ServerConfig> S3Servers { get; } = new();

    // Editing state
    [ObservableProperty] private bool _isEditingWebDav;
    [ObservableProperty] private WebDavServerConfig? _editingWebDav;

    [ObservableProperty] private bool _isEditingOneDrive;
    [ObservableProperty] private OneDriveServerConfig? _editingOneDrive;

    [ObservableProperty] private bool _isEditingS3;
    [ObservableProperty] private S3ServerConfig? _editingS3;

    private readonly IConfigurationService _configService;

    public SettingsViewModel(IConfigurationService configService)
    {
        _configService = configService;
    }

    public async Task InitializeAsync()
    {
        await LoadSettingsAsync();
        EverythingStatusText = System.Diagnostics.Process.GetProcessesByName("Everything").Length > 0
            ? "Running" : "Not Running";
        SyncthingStatusText = await CheckSyncthingAsync() ? "Running" : "Not Running";
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _configService.GetSettingsAsync();
            if (settings == null)
            {
                Debug.WriteLine("[Settings] Settings file not found, using defaults");
                return;
            }

            EverythingPath = settings.EverythingPath;
            SyncthingPath = settings.SyncthingPath;
            SyncthingApiUrl = settings.SyncthingApiUrl;
            MinimizeToTray = settings.MinimizeToTray;
            AutoStart = settings.AutoStart;
            LogRetentionDays = settings.LogRetentionDays;

            WebDavServers.Clear();
            if (settings.WebDavServers != null)
                foreach (var s in settings.WebDavServers) WebDavServers.Add(s);

            OneDriveAccounts.Clear();
            if (settings.OneDriveAccounts != null)
                foreach (var s in settings.OneDriveAccounts) OneDriveAccounts.Add(s);

            S3Servers.Clear();
            if (settings.S3Servers != null)
                foreach (var s in settings.S3Servers) S3Servers.Add(s);

            Debug.WriteLine($"[Settings] Loaded: {WebDavServers.Count} WebDAV, {OneDriveAccounts.Count} OneDrive, {S3Servers.Count} S3");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Load failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new AppSettings
            {
                EverythingPath = EverythingPath ?? string.Empty,
                SyncthingPath = SyncthingPath ?? string.Empty,
                SyncthingApiUrl = SyncthingApiUrl ?? string.Empty,
                MinimizeToTray = MinimizeToTray,
                AutoStart = AutoStart,
                LogRetentionDays = LogRetentionDays,
                WebDavServers = new List<WebDavServerConfig>(WebDavServers),
                OneDriveAccounts = new List<OneDriveServerConfig>(OneDriveAccounts),
                S3Servers = new List<S3ServerConfig>(S3Servers)
            };

            await _configService.SaveSettingsAsync(settings);
            Debug.WriteLine("[Settings] Save completed successfully!");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Save FAILED: {ex.Message}");
        }
    }

    private async Task<bool> CheckSyncthingAsync()
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync($"{SyncthingApiUrl}/rest/system/status");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // WebDAV
    [RelayCommand]
    private void AddWebDav()
    {
        EditingWebDav = new WebDavServerConfig();
        IsEditingWebDav = true;
    }

    [RelayCommand]
    private void EditWebDav(WebDavServerConfig? server)
    {
        if (server == null) return;
        EditingWebDav = server;
        IsEditingWebDav = true;
    }

    [RelayCommand]
    private async Task SaveWebDavAsync()
    {
        if (EditingWebDav == null) return;

        if (!WebDavServers.Any(s => s.Id == EditingWebDav.Id))
            WebDavServers.Add(EditingWebDav);

        IsEditingWebDav = false;
        EditingWebDav = null;
        await SaveSettingsAsync();
    }

    [RelayCommand]
    private void CancelWebDav()
    {
        IsEditingWebDav = false;
        EditingWebDav = null;
    }

    [RelayCommand]
    private void DeleteWebDav(WebDavServerConfig? server)
    {
        if (server != null) WebDavServers.Remove(server);
    }

    // OneDrive
    [RelayCommand]
    private void AddOneDrive()
    {
        EditingOneDrive = new OneDriveServerConfig();
        IsEditingOneDrive = true;
    }

    [RelayCommand]
    private void EditOneDrive(OneDriveServerConfig? account)
    {
        if (account == null) return;
        EditingOneDrive = account;
        IsEditingOneDrive = true;
    }

    [RelayCommand]
    private async Task SaveOneDriveAsync()
    {
        if (EditingOneDrive == null) return;
        if (!OneDriveAccounts.Any(a => a.Id == EditingOneDrive.Id))
            OneDriveAccounts.Add(EditingOneDrive);
        IsEditingOneDrive = false;
        EditingOneDrive = null;
        await SaveSettingsAsync();
    }

    [RelayCommand]
    private void CancelOneDrive()
    {
        IsEditingOneDrive = false;
        EditingOneDrive = null;
    }

    [RelayCommand]
    private void DeleteOneDrive(OneDriveServerConfig? account)
    {
        if (account != null) OneDriveAccounts.Remove(account);
    }

    // S3
    [RelayCommand]
    private void AddS3()
    {
        EditingS3 = new S3ServerConfig();
        IsEditingS3 = true;
    }

    [RelayCommand]
    private void EditS3(S3ServerConfig? server)
    {
        if (server == null) return;
        EditingS3 = server;
        IsEditingS3 = true;
    }

    [RelayCommand]
    private async Task SaveS3Async()
    {
        if (EditingS3 == null) return;
        if (!S3Servers.Any(s => s.Id == EditingS3.Id))
            S3Servers.Add(EditingS3);
        IsEditingS3 = false;
        EditingS3 = null;
        await SaveSettingsAsync();
    }

    [RelayCommand]
    private void CancelS3()
    {
        IsEditingS3 = false;
        EditingS3 = null;
    }

    [RelayCommand]
    private void DeleteS3(S3ServerConfig? server)
    {
        if (server != null) S3Servers.Remove(server);
    }
}
