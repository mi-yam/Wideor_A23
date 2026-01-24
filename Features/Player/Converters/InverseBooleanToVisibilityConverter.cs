using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wideor.App.Features.Player
{
    /// <summary>
    /// ブール値をVisibilityに変換するコンバーター（逆変換版）
    /// true → Collapsed, false → Visible
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Collapsed;
            }
            return false;
        }
    }
}
