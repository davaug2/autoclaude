using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace AutoClaude.App.Controls;

public sealed class SplitterBar : Control
{
    public SplitterBar()
    {
        Height = 6;
        MinHeight = 6;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        IsTabStop = false;

        Template = (ControlTemplate)XamlReader.Load(
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Border Background=\"{TemplateBinding Background}\" " +
            "BorderBrush=\"{TemplateBinding BorderBrush}\" " +
            "BorderThickness=\"{TemplateBinding BorderThickness}\" />" +
            "</ControlTemplate>");
    }
}
