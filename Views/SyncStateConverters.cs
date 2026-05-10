using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NotionPad.Models;

namespace NotionPad.Views;

public class SyncStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            SyncState.Idle => "OkBrush",
            SyncState.Pending => "WarnBrush",
            SyncState.Syncing => "WarnBrush",
            SyncState.Error => "ErrorBrush",
            _ => "MutedBrush"
        };
        return Application.Current.Resources[key] ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SyncStateToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            SyncState.Idle => "Synced",
            SyncState.Pending => "Pending",
            SyncState.Syncing => "Syncing",
            SyncState.Error => "Error",
            _ => string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ErrorVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SyncState.Error ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
