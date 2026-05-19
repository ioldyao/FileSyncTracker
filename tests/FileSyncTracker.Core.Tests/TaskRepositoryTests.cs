using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using Xunit;

namespace FileSyncTracker.Core.Tests;

public class TaskRepositoryTests : IDisposable
{
    private readonly TaskRepository _repository;
    private readonly string _testFilePath;

    public TaskRepositoryTests()
    {
        _repository = new TaskRepository();
        _testFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileSyncTracker", "tasks.json");
    }

    [Fact]
    public async Task AddAsync_ShouldAddTask()
    {
        var task = new SyncTask
        {
            Name = "Test Task",
            Type = SyncTaskType.Folder,
            OriginalPath = @"C:\Test"
        };

        await _repository.AddAsync(task);

        var tasks = await _repository.GetAllAsync();
        Assert.Contains(tasks, t => t.Name == "Test Task");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveTask()
    {
        var task = new SyncTask { Name = "Delete Me", Type = SyncTaskType.Folder };
        await _repository.AddAsync(task);

        await _repository.DeleteAsync(task.Id);

        var tasks = await _repository.GetAllAsync();
        Assert.DoesNotContain(tasks, t => t.Id == task.Id);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateTask()
    {
        var task = new SyncTask { Name = "Original", Type = SyncTaskType.Folder };
        await _repository.AddAsync(task);

        task.Name = "Updated";
        await _repository.UpdateAsync(task);

        var tasks = await _repository.GetAllAsync();
        var updated = tasks.FirstOrDefault(t => t.Id == task.Id);
        Assert.Equal("Updated", updated?.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnCorrectTask()
    {
        var task = new SyncTask { Name = "Find Me", Type = SyncTaskType.SingleFile };
        await _repository.AddAsync(task);

        var result = await _repository.GetByIdAsync(task.Id);
        Assert.NotNull(result);
        Assert.Equal("Find Me", result.Name);
    }

    public void Dispose()
    {
        // Cleanup test data
        if (File.Exists(_testFilePath))
        {
            try { File.WriteAllText(_testFilePath, "[]"); } catch { }
        }
    }
}
