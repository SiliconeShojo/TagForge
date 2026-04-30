using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TagForge.Converters
{
    public class SessionTypeColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string type)
            {
                if (type == "chat") return SolidColorBrush.Parse("#6B8AFF");
                if (type == "tag" || type == "generator") return SolidColorBrush.Parse("#4CAF50");
            }
            return SolidColorBrush.Parse("#888888");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class SessionTypeIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string type)
            {
                if (type == "chat") return "💬"; // Chat bubble
                if (type == "tag" || type == "generator") return "🏷️"; // Tag
            }
            return "❓";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
