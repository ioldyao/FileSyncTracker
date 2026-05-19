using Avalonia.Controls;
using Avalonia.Media;
using FileSyncTracker.Core.Models;

namespace FileSyncTracker.UI.Controls;

public partial class StatusBadge : UserControl
{
    public StatusBadge()
    {
        InitializeComponent();
    }

    public void SetStatus(SyncStatus status)
    {
        BadgeText.Text = status.ToString();
        BadgeBorder.Background = status switch
        {
            SyncStatus.Idle => new SolidColorBrush(Color.Parse("#A6E3A1")),
            SyncStatus.Syncing => new SolidColorBrush(Color.Parse("#89B4FA")),
            SyncStatus.Tracking => new SolidColorBrush(Color.Parse("#FAB387")),
            SyncStatus.Error => new SolidColorBrush(Color.Parse("#F38BA8")),
            SyncStatus.Disabled => new SolidColorBrush(Color.Parse("#6C7086")),
            _ => new SolidColorBrush(Color.Parse("#6C7086"))
        };
    }
}
