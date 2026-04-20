using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AutoClaude.App.Converters;

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isEmpty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection c => c.Count == 0,
            _ => false
        };

        var invert = Invert || (parameter is string p && string.Equals(p, "Invert", StringComparison.OrdinalIgnoreCase));
        var visible = invert ? isEmpty : !isEmpty;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
