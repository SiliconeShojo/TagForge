using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TagForge.Converters
{
    public class RoleToAvatarBackgroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role)
            {
                return role switch
                {
                    "User" => new SolidColorBrush(Color.Parse("#6B8AFF")),  // Accent blue/purple
                    "Assistant" => new SolidColorBrush(Color.Parse("#4CAF50")),  // Green for AI
                    "System" or "Error" => new SolidColorBrush(Color.Parse("#555555")),  // Gray
                    _ => new SolidColorBrush(Color.Parse("#555555"))
                };
            }
            
            return new SolidColorBrush(Color.Parse("#555555"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
