using Avalonia.Data.Converters;
using FileSyncTracker.Core.Models;
using System;
using System.Globalization;

namespace FileSyncTracker.UI.Converters;

public class BoolToIconConverter : IValueConverter
{
    public static readonly BoolToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
            return isEnabled ? "Disable" : "Enable";
        return "Enable";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TypeToIconConverter : IValueConverter
{
    public static readonly TypeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SyncTaskType type)
            return type == SyncTaskType.Folder ? "📁" : "📄";
        return "📄";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StepToFillConverter : IValueConverter
{
    public static readonly StepToFillConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int currentStep && parameter is string stepStr && int.TryParse(stepStr, out var step))
        {
            var brush = currentStep >= step
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5B6CF8"))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#45475A"));
            return brush;
        }
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#45475A"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class IntToBoolConverter : IValueConverter
{
    public static readonly IntToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int currentStep && parameter is string stepStr && int.TryParse(stepStr, out var step))
            return currentStep == step;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class GreaterThanOneConverter : IValueConverter
{
    public static readonly GreaterThanOneConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int step) return step > 1;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LessThanFourConverter : IValueConverter
{
    public static readonly LessThanFourConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int step) return step < 4;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class EqualToFourConverter : IValueConverter
{
    public static readonly EqualToFourConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int step) return step == 4;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToToggleConverter : IValueConverter
{
    public static readonly BoolToToggleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
            return isEnabled ? "On" : "Off";
        return "Off";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TypeToSelectionConverter : IValueConverter
{
    public static readonly TypeToSelectionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SyncTaskType selectedType && parameter is string target)
        {
            bool isSelected = (target == "Folder" && selectedType == SyncTaskType.Folder)
                           || (target == "File" && selectedType == SyncTaskType.SingleFile);
            return new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse(isSelected ? "#5B6CF8" : "#45475A"));
        }
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#45475A"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StorageToSelectionConverter : IValueConverter
{
    public static readonly StorageToSelectionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StorageType selectedType && parameter is string target)
        {
            var targetEnum = target switch
            {
                "Local" => StorageType.Local,
                "WebDAV" => StorageType.WebDAV,
                "OneDrive" => StorageType.OneDrive,
                "S3" => StorageType.S3,
                _ => StorageType.Local
            };
            bool isSelected = selectedType == targetEnum;
            return new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse(isSelected ? "#5B6CF8" : "#45475A"));
        }
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#45475A"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
