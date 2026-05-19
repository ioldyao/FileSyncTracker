using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.ViewModels;

public partial class TaskListViewModel : ObservableObject
{
    private readonly ITaskRepository _taskRepository;
    private readonly ISyncthingService _syncthingService;
    private readonly IFileTrackerService _fileTrackerService;
    private readonly ISyncSchedulerService _syncSchedulerService;

    public ObservableCollection<SyncTask> Tasks { get; } = new();

    public TaskListViewModel(
        ITaskRepository taskRepository,
        ISyncthingService syncthingService,
        IFileTrackerService fileTrackerService,
        ISyncSchedulerService syncSchedulerService)
    {
        _taskRepository = taskRepository;
        _syncthingService = syncthingService;
        _fileTrackerService = fileTrackerService;
        _syncSchedulerService = syncSchedulerService;
        _ = LoadTasksAsync();
    }

    private async Task LoadTasksAsync()
    {
        var tasks = await _taskRepository.GetAllAsync();
        Tasks.Clear();
        foreach (var task in tasks)
            Tasks.Add(task);
    }

    public async Task RefreshAsync()
    {
        await LoadTasksAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync(SyncTask task)
    {
        if (task == null) return;

        task.Status = SyncStatus.Syncing;
        task.UpdatedAt = System.DateTime.Now;
        await _taskRepository.UpdateAsync(task);

        try
        {
            await _syncSchedulerService.TriggerNowAsync(task.Id);
            task.Status = SyncStatus.Idle;
            task.LastSyncTime = System.DateTime.Now;
        }
        catch (System.Exception ex)
        {
            task.Status = SyncStatus.Error;
            task.LastError = ex.Message;
        }

        await _taskRepository.UpdateAsync(task);
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(SyncTask task)
    {
        if (task == null) return;

        task.IsEnabled = !task.IsEnabled;
        task.Status = task.IsEnabled ? SyncStatus.Idle : SyncStatus.Disabled;
        task.UpdatedAt = System.DateTime.Now;
        await _taskRepository.UpdateAsync(task);
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(SyncTask task)
    {
        if (task == null) return;

        await _fileTrackerService.StopTrackingAsync(task.Id);
        await _syncSchedulerService.UnscheduleAsync(task.Id);
        await _taskRepository.DeleteAsync(task.Id);
        Tasks.Remove(task);
    }

    [RelayCommand]
    private void EditTask(SyncTask? task)
    {
        if (task == null) return;
        AddTaskViewModel.EditingTask = task;
        FileSyncTracker.UI.Views.MainWindow.Instance.ShowPage("AddTask");
    }
}
