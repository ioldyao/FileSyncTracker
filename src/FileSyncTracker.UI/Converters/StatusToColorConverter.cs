using Avalonia.Data.Converters;
using Avalonia.Media;
using FileSyncTracker.Core.Models;
using System;
using System.Globalization;

namespace FileSyncTracker.UI.Converters;

public class StatusToColorConverter : IValueConverter
{
    public static readonly StatusToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SyncStatus status)
        {
            return status switch
            {
                SyncStatus.Idle => new SolidColorBrush(Color.Parse("#A6E3A1")),
                SyncStatus.Syncing => new SolidColorBrush(Color.Parse("#89B4FA")),
                SyncStatus.Tracking => new SolidColorBrush(Color.Parse("#FAB387")),
                SyncStatus.Error => new SolidColorBrush(Color.Parse("#F38BA8")),
                SyncStatus.Disabled => new SolidColorBrush(Color.Parse("#6C7086")),
                _ => new SolidColorBrush(Color.Parse("#6C7086"))
            };
        }
        return new SolidColorBrush(Color.Parse("#6C7086"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
