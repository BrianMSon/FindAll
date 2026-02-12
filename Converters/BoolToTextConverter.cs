using Avalonia.Data.Converters;
using System.Globalization;

namespace FindAll.Converters;

public class BoolToTextConverter : IValueConverter
{
    public static readonly BoolToTextConverter PauseResume = new()
    {
        TrueText = "Resume",
        FalseText = "Pause"
    };

    public string TrueText { get; set; } = "True";
    public string FalseText { get; set; } = "False";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueText : FalseText;
        return FalseText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
