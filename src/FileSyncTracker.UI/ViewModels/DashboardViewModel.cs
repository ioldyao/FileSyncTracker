using CommunityToolkit.Mvvm.ComponentModel;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ITaskRepository _taskRepository;

    [ObservableProperty]
    private int _totalTasks;

    [ObservableProperty]
    private int _syncingTasks;

    [ObservableProperty]
    private int _trackingTasks;

    public ObservableCollection<SyncTask> RecentActivity { get; } = new();

    public DashboardViewModel(ITaskRepository taskRepository)
    {
        _taskRepository = taskRepository;
    }

    public async Task InitializeAsync()
    {
        var tasks = await _taskRepository.GetAllAsync();
        TotalTasks = tasks.Count;
        SyncingTasks = tasks.Count(t => t.Status == SyncStatus.Syncing);
        TrackingTasks = tasks.Count(t => t.Status == SyncStatus.Tracking);

        RecentActivity.Clear();
        foreach (var task in tasks.Where(t => t.LastSyncTime != null).OrderByDescending(t => t.LastSyncTime).Take(10))
            RecentActivity.Add(task);
    }

    public async Task RefreshAsync()
    {
        await InitializeAsync();
    }
}
