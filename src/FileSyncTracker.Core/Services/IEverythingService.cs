using FileSyncTracker.Core.Models;

namespace FileSyncTracker.Core.Services;

public interface IEverythingService
{
    bool IsAvailable { get; }
    string? ErrorMessage { get; }
    Task<string?> FindFileAsync(FileIdentity identity);
    Task<List<string>> SearchAsync(string query, int maxResults = 10);
}
