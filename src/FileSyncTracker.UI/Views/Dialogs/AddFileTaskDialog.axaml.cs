using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FileSyncTracker.UI.Views.Dialogs;

public partial class AddFileTaskDialog : Window
{
    public string TaskName => TaskNameBox.Text ?? string.Empty;
    public string LocalPath => LocalPathBox.Text ?? string.Empty;
    public string RemoteTarget => RemoteTargetBox.Text ?? string.Empty;

    public AddFileTaskDialog()
    {
        InitializeComponent();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
