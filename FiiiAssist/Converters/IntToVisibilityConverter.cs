using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FiiiAssist.Converters;

/// <summary>
/// Converts an integer to <see cref="Visibility"/>.
/// By default, non-zero values are Visible and zero is Collapsed.
/// Pass "Invert" as the converter parameter to reverse the logic
/// (zero is Visible, non-zero is Collapsed).
/// </summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int count = value is int i ? i : 0;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool isNonZero = count != 0;
        return (isNonZero ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
