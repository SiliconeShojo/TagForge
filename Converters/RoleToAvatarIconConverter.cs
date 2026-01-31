using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace TagForge.Converters
{
    public class RoleToAvatarIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string role)
            {
                return role switch
                {
                    "User" => "M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z",
                    // Robot head icon for AI
                    "Assistant" => "M12,2A2,2 0 0,1 14,4C14,4.74 13.6,5.39 13,5.73V7H14A4,4 0 0,1 18,11V13H22V17H18V20A2,2 0 0,1 16,22H8A2,2 0 0,1 6,20V17H2V13H6V11A4,4 0 0,1 10,7H11V5.73C10.4,5.39 10,4.74 10,4A2,2 0 0,1 12,2M7.5,13A0.5,0.5 0 0,0 7,13.5A0.5,0.5 0 0,0 7.5,14A0.5,0.5 0 0,0 8,13.5A0.5,0.5 0 0,0 7.5,13M16.5,13A0.5,0.5 0 0,0 16,13.5A0.5,0.5 0 0,0 16.5,14A0.5,0.5 0 0,0 17,13.5A0.5,0.5 0 0,0 16.5,13Z",
                    "System" or "Error" => "M13,9H11V7H13M13,17H11V11H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                    _ => "M13,9H11V7H13M13,17H11V11H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z"
                };
            }
            
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
