using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using Xunit;

namespace FileSyncTracker.Core.Tests;

public class TaskRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TaskRepository _repository;

    public TaskRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileSyncTrackerTests", Guid.NewGuid().ToString());
        _repository = new TaskRepository(_tempDir);
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
        Assert.NotNull(updated);
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

    [Fact]
    public async Task GetAllAsync_ShouldReturnEmpty_WhenNoTasksExist()
    {
        var tasks = await _repository.GetAllAsync();
        Assert.Empty(tasks);
    }

    [Fact]
    public async Task DeleteAsync_ShouldNotThrow_WhenTaskDoesNotExist()
    {
        await _repository.DeleteAsync(Guid.NewGuid());
        var tasks = await _repository.GetAllAsync();
        Assert.Empty(tasks);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
