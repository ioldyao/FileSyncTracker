using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileSyncTracker.Core.Models;
using FileSyncTracker.UI.ViewModels;
using System.Linq;

namespace FileSyncTracker.UI.Views.Pages;

public partial class AddTaskPage : UserControl
{
    public AddTaskPage()
    {
        InitializeComponent();
        var vm = App.Services?.GetService(typeof(AddTaskViewModel)) as AddTaskViewModel;
        DataContext = vm;
        if (vm != null)
        {
            vm.TaskCreated += (_, _) =>
            {
                Dispatcher.UIThread.Post(() => MainWindow.Instance.ShowPage("Tasks"));
            };
            vm.ErrorOccurred += (_, msg) =>
            {
                Dispatcher.UIThread.Post(() => System.Diagnostics.Debug.WriteLine($"CreateTask error: {msg}"));
            };
            _ = vm.LoadAvailableServersAsync();

            // Load editing task data
            if (AddTaskViewModel.EditingTask is SyncTask editTask)
            {
                vm.TaskName = editTask.Name;
                vm.SelectedType = editTask.Type;
                vm.LocalPath = editTask.CurrentPath ?? editTask.OriginalPath;
                vm.SelectedMode = editTask.Mode;
                vm.CronExpression = editTask.CronExpression ?? string.Empty;
                vm.RemotePath = editTask.RemotePath;
                vm.DownloadPath = editTask.DownloadPath ?? string.Empty;
                if (editTask.StorageTargets != null)
                    foreach (var t in editTask.StorageTargets)
                        vm.SelectedTargets.Add(t);
            }
        }
    }

    private void OnFolderSelected(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AddTaskViewModel vm)
            vm.SelectedType = SyncTaskType.Folder;
    }

    private void OnFileSelected(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AddTaskViewModel vm)
            vm.SelectedType = SyncTaskType.SingleFile;
    }

    private void OnServerCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag == null || DataContext is not AddTaskViewModel vm) return;

        var target = border.Tag switch
        {
            WebDavServerConfig w => new StorageTarget { Type = StorageType.WebDAV, ServerId = w.Id, ServerName = w.Name, RemotePath = vm.RemotePath },
            OneDriveServerConfig o => new StorageTarget { Type = StorageType.OneDrive, ServerId = o.Id, ServerName = o.Name, RemotePath = vm.RemotePath },
            S3ServerConfig s => new StorageTarget { Type = StorageType.S3, ServerId = s.Id, ServerName = s.Name, RemotePath = vm.RemotePath },
            _ => null
        };

        if (target == null) return;

        var existing = vm.SelectedTargets.FirstOrDefault(t =>
            t.Type == target.Type && t.ServerId == target.ServerId);

        if (existing != null)
            vm.SelectedTargets.Remove(existing);
        else
            vm.SelectedTargets.Add(target);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not AddTaskViewModel vm) return;

        if (vm.SelectedType == SyncTaskType.Folder)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Folder",
                AllowMultiple = false
            });
            if (folders.Count > 0)
                vm.LocalPath = folders[0].Path.LocalPath;
        }
        else
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select File",
                AllowMultiple = false
            });
            if (files.Count > 0)
                vm.LocalPath = files[0].Path.LocalPath;
        }
    }

    private void OnPrevStepClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddTaskViewModel vm) vm.PrevStep();
    }

    private void OnNextStepClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddTaskViewModel vm) vm.NextStep();
    }

    private async void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddTaskViewModel vm) await vm.CreateTaskAsync();
    }
}
