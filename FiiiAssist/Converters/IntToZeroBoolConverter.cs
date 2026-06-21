using Microsoft.UI.Xaml.Data;
using System;

namespace FiiiAssist.Converters;

/// <summary>
/// Converts an integer to a boolean.
/// Returns true when the value equals zero, false otherwise.
/// Useful for showing "no items" warnings.
/// </summary>
public sealed class IntToZeroBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int count = value is int i ? i : 0;
        return count == 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
