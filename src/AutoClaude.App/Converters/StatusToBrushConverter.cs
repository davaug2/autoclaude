using AutoClaude.Core.Domain.Enums;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AutoClaude.App.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not SessionStatus status)
            return new SolidColorBrush(Colors.Gray);

        var color = status switch
        {
            SessionStatus.Running => Colors.LimeGreen,
            SessionStatus.Completed => Colors.Green,
            SessionStatus.Failed => Colors.Red,
            SessionStatus.Paused => Colors.Orange,
            _ => Colors.Gray
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
