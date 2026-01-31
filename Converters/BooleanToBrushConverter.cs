using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TagForge.Converters
{
    /// <summary>
    /// Converts boolean to brush based on parameter format: "TrueBrush|FalseBrush"
    /// Example: "{Binding IsActive, Converter={StaticResource BoolToBrush}, ConverterParameter='#6B8AFF|Transparent'}"
    /// </summary>
    public class BooleanToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string param)
            {
                var parts = param.Split('|');
                if (parts.Length == 2)
                {
                    var trueBrush = parts[0].Trim();
                    var falseBrush = parts[1].Trim();
                    
                    var colorString = boolValue ? trueBrush : falseBrush;
                    
                    if (colorString.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
                        return Brushes.Transparent;
                    
                    return new SolidColorBrush(Color.Parse(colorString));
                }
            }
            
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
