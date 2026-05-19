namespace FileSyncTracker.Core.Services;

public class SyncthingFolderStatus
{
    public int GlobalFiles { get; set; }
    public int LocalFiles { get; set; }
    public int NeedFiles { get; set; }
    public string State { get; set; } = string.Empty;
}

public interface ISyncthingService
{
    Task<bool> IsRunningAsync();
    Task<string> GetApiKeyAsync();
    Task AddFolderAsync(string localPath, string folderId, string remoteTarget);
    Task RemoveFolderAsync(string folderId);
    Task TriggerSyncAsync(string folderId);
    Task<SyncthingFolderStatus?> GetFolderStatusAsync(string folderId);
}
