using AutoClaude.App.ViewModels.SessionEvents;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace AutoClaude.App.Converters;

public sealed class InfoSeverityToInfoBarSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not InfoSeverity s) return InfoBarSeverity.Informational;
        return s switch
        {
            InfoSeverity.Warning => InfoBarSeverity.Warning,
            InfoSeverity.Error => InfoBarSeverity.Error,
            InfoSeverity.Success => InfoBarSeverity.Success,
            _ => InfoBarSeverity.Informational
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
