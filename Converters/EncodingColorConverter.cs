using System.Globalization;
using Avalonia.Data.Converters;

namespace FindAll.Converters;

public class EncodingColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string encoding)
        {
            return encoding switch
            {
                "UTF-8" => Avalonia.Media.Brushes.ForestGreen,
                "UTF-8 BOM" => Avalonia.Media.Brushes.DarkGreen,
                "ASCII" => Avalonia.Media.Brushes.Gray,
                "ANSI" => Avalonia.Media.Brushes.DarkOrange,
                "UTF-16 LE" or "UTF-16 BE" => Avalonia.Media.Brushes.DodgerBlue,
                "UTF-32 LE" or "UTF-32 BE" => Avalonia.Media.Brushes.MediumPurple,
                "Binary" => Avalonia.Media.Brushes.Red,
                _ => Avalonia.Media.Brushes.Gray
            };
        }
        return Avalonia.Media.Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
