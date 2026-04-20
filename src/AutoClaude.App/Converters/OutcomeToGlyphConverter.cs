using AutoClaude.Core.Domain.Enums;
using Microsoft.UI.Xaml.Data;

namespace AutoClaude.App.Converters;

public sealed class OutcomeToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ExecutionOutcome outcome)
            return "\uE916"; // Clock

        return outcome switch
        {
            ExecutionOutcome.Success => "\uE930",    // Completed (check)
            ExecutionOutcome.Failure => "\uE783",    // Error
            ExecutionOutcome.Timeout => "\uE916",    // Clock
            ExecutionOutcome.Cancelled => "\uE711",  // Cancel
            _ => "\uE895"                            // Sync / In progress
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
