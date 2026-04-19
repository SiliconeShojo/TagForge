using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TagForge.Converters
{
    public class RoleToAlignmentConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role && role == "User")
                return HorizontalAlignment.Right;
            return HorizontalAlignment.Left;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RoleToBackgroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role && role == "User")
            {
                // Return Accent Color (DynamicResource would be handled by XAML, here we return a specific color or use binding)
                // Since we can't easily return a DynamicResource here, we might return a brush if we had access to resources.
                // Better approach: Use Classes="user" or "assistant" and styled in XAML.
                // But user wants "remove theming".
                // I'll return a hardcoded Blue or similar for User, Dark Gray for AI.
                return SolidColorBrush.Parse("#2D2D2D"); 
            }
            return SolidColorBrush.Parse("#1A1A1A");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RoleToCornerRadiusConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role && role == "User")
                return new CornerRadius(15, 15, 0, 15);
            return new CornerRadius(15, 15, 15, 0);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
