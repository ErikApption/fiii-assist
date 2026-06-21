using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using FiiiAssist.ViewModels;
using System;

namespace FiiiAssist.Converters;

/// <summary>
/// Converts a <see cref="WizardStep"/> value to <see cref="Visibility"/>.
/// Pass the target step name as the converter parameter (e.g. "AccountSelection").
/// The element is Visible when the current step matches the parameter, Collapsed otherwise.
/// </summary>
public sealed class WizardStepToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is WizardStep current && parameter is string stepName)
        {
            if (Enum.TryParse<WizardStep>(stepName, out var target))
            {
                return current == target ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a <see cref="WizardStep"/> value to <see cref="Visibility"/>.
/// The element is Visible when the step is NOT Closed, Collapsed when Closed.
/// </summary>
public sealed class WizardStepNotClosedToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is WizardStep step)
        {
            return step != WizardStep.Closed ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a string to <see cref="Visibility"/>.
/// Visible when the string is not null/empty, Collapsed otherwise.
/// Pass "Invert" as parameter to invert the logic.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool hasValue = value is string s && !string.IsNullOrEmpty(s);
        bool invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return (hasValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
