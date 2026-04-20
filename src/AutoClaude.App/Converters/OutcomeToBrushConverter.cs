using AutoClaude.Core.Domain.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AutoClaude.App.Converters;

public sealed class OutcomeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ExecutionOutcome outcome)
            return GetResource("TextFillColorSecondaryBrush");

        var resourceKey = outcome switch
        {
            ExecutionOutcome.Success => "SystemFillColorSuccessBrush",
            ExecutionOutcome.Failure => "SystemFillColorCriticalBrush",
            ExecutionOutcome.Timeout => "SystemFillColorCautionBrush",
            ExecutionOutcome.Cancelled => "TextFillColorSecondaryBrush",
            _ => "SystemAccentColorLight2"
        };

        return GetResource(resourceKey);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    private static Brush GetResource(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var res))
        {
            if (res is Brush brush) return brush;
            if (res is Windows.UI.Color color) return new SolidColorBrush(color);
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
