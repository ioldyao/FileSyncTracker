using System.Text.Json;
using FileSyncTracker.Core.Models;

namespace FileSyncTracker.Core.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<SyncTask> _tasks = new();

    public TaskRepository() : this(null) { }

    public TaskRepository(string? basePath)
    {
        var appData = basePath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "FileSyncTracker");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "tasks.json");
    }

    public async Task<List<SyncTask>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath))
                return new List<SyncTask>();

            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<SyncTask>>(json) ?? new List<SyncTask>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SyncTask?> GetByIdAsync(Guid id)
    {
        var tasks = await GetAllAsync();
        return tasks.FirstOrDefault(t => t.Id == id);
    }

    public async Task SaveAsync(List<SyncTask> tasks)
    {
        await _lock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(tasks, options);
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddAsync(SyncTask task)
    {
        var tasks = await GetAllAsync();
        tasks.Add(task);
        await SaveAsync(tasks);
    }

    public async Task UpdateAsync(SyncTask task)
    {
        var tasks = await GetAllAsync();
        var index = tasks.FindIndex(t => t.Id == task.Id);
        if (index >= 0)
        {
            tasks[index] = task;
            await SaveAsync(tasks);
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var tasks = await GetAllAsync();
        tasks.RemoveAll(t => t.Id == id);
        await SaveAsync(tasks);
    }
}
