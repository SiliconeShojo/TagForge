using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TagForge.Converters
{
    /// <summary>
    /// Returns the Grid column index for the avatar based on the message role.
    /// User: Column 1 (right side), AI: Column 0 (left side)
    /// </summary>
    public class RoleToAvatarColumnConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role && role == "User")
                return 1; // Avatar on right for user
            return 0; // Avatar on left for AI
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns the Grid column index for the message bubble based on the message role.
    /// User: Column 0 (left of avatar), AI: Column 1 (right of avatar)
    /// </summary>
    public class RoleToMessageColumnConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role && role == "User")
                return 0; // Message on left (avatar on right)
            return 1; // Message on right (avatar on left)
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns the margin for the avatar based on the message role.
    /// User: Left margin (space between message and avatar), AI: Right margin
    /// </summary>
    public class RoleToAvatarMarginConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role && role == "User")
                return new Avalonia.Thickness(10, 0, 0, 0); // Left margin for user (avatar on right)
            return new Avalonia.Thickness(0, 0, 10, 0); // Right margin for AI (avatar on left)
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
