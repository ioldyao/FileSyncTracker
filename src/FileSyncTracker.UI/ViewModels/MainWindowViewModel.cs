using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileSyncTracker.Core.Models;
using FileSyncTracker.Core.Repositories;
using FileSyncTracker.Core.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace FileSyncTracker.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ITaskRepository _taskRepository;
    private readonly IEverythingService _everythingService;
    private readonly ISyncthingService _syncthingService;

    [ObservableProperty]
    private string _everythingStatus = "Disconnected";

    [ObservableProperty]
    private string _syncthingStatus = "Stopped";

    [ObservableProperty]
    private object? _currentPage;

    public ObservableCollection<SyncTask> Tasks { get; } = new();

    public MainWindowViewModel(
        ITaskRepository taskRepository,
        IEverythingService everythingService,
        ISyncthingService syncthingService)
    {
        _taskRepository = taskRepository;
        _everythingService = everythingService;
        _syncthingService = syncthingService;
    }

    public async Task InitializeAsync()
    {
        EverythingStatus = _everythingService.IsAvailable ? "Connected" : "Disconnected";
        SyncthingStatus = await _syncthingService.IsRunningAsync() ? "Running" : "Stopped";

        var tasks = await _taskRepository.GetAllAsync();
        Tasks.Clear();
        foreach (var task in tasks)
            Tasks.Add(task);
    }
}
