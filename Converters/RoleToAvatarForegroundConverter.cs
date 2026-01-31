using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TagForge.Converters
{
    public class RoleToAvatarForegroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role)
            {
                // All avatars use white foreground for good contrast
                return new SolidColorBrush(Colors.White);
            }
            
            return new SolidColorBrush(Colors.White);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
