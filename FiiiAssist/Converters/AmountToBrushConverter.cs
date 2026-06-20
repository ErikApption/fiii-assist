using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace FiiiAssist.Converters;

/// <summary>
/// Converts a decimal amount to a colored brush:
/// negative amounts are red, positive amounts are green, zero is default text color.
/// </summary>
public sealed class AmountToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal amount)
        {
            if (amount < 0)
                return new SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
            if (amount > 0)
                return new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
