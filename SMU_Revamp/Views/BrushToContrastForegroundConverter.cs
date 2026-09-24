using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SMU_Revamp.Views
{
    /// <summary>
    /// Returns black foreground on light backgrounds and white foreground on dark
    /// backgrounds, based on relative luminance. Used for text on heatmap cells.
    /// </summary>
    public class BrushToContrastForegroundConverter : IValueConverter
    {
        public static readonly BrushToContrastForegroundConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Color color;
            switch (value)
            {
                case SolidColorBrush solid:
                    color = solid.Color;
                    break;
                case IBrush brush when brush is ISolidColorBrush solidBase:
                    color = solidBase.Color;
                    break;
                case Color c:
                    color = c;
                    break;
                case string s when !string.IsNullOrWhiteSpace(s):
                    try
                    {
                        color = Color.Parse(s);
                    }
                    catch
                    {
                        return Brushes.Black;
                    }
                    break;
                default:
                    return Brushes.Black;
            }

            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luminance > 0.6 ? Brushes.Black : Brushes.White;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
