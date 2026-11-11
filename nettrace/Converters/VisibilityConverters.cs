using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace NetTrace.Converters;

/// <summary>maps a bool to visibility, optionally inverted.</summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public bool IsInverted { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;
        return flag ^ IsInverted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility v && (v == Visibility.Visible) ^ IsInverted;
}

/// <summary>collapses an element when the bound string is null or empty.</summary>
public sealed partial class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
