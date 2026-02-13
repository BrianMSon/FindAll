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

    public static readonly BoolToTextConverter ExpandCollapse = new()
    {
        TrueText = "▼",
        FalseText = "▶"
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

public class NullableBoolToTextConverter : IValueConverter
{
    public static readonly NullableBoolToTextConverter NameSearchScope = new()
    {
        FalseText = "File",
        NullText = "File+Folder",
        TrueText = "Folder"
    };

    public static readonly NullableBoolToTextConverter SortDirection = new()
    {
        NullText = "Sort",
        TrueText = "Sort ▲",
        FalseText = "Sort ▼"
    };

    public static readonly NullableBoolToTextConverter SizeSortDirection = new()
    {
        NullText = "Size",
        TrueText = "Size ▲",
        FalseText = "Size ▼"
    };

    public string TrueText { get; set; } = "True";
    public string FalseText { get; set; } = "False";
    public string NullText { get; set; } = "Null";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueText : FalseText;
        return NullText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
