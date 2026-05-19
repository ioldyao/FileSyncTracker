using FileSyncTracker.Core.Models;

namespace FileSyncTracker.Core.Repositories;

public interface ITaskRepository
{
    Task<List<SyncTask>> GetAllAsync();
    Task<SyncTask?> GetByIdAsync(Guid id);
    Task SaveAsync(List<SyncTask> tasks);
    Task AddAsync(SyncTask task);
    Task UpdateAsync(SyncTask task);
    Task DeleteAsync(Guid id);
}
