using System;
using System.Globalization;
using System.Windows.Data;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// 時間（秒）をY座標（ピクセル）に変換するコンバーター
    /// 固定スケール: 1秒 = 100ピクセル
    /// </summary>
    public class TimeToYConverter : IValueConverter
    {
        private const double PixelsPerSecond = 100.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double time && time >= 0)
            {
                return time * PixelsPerSecond;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double y && y >= 0)
            {
                return y / PixelsPerSecond;
            }
            return 0.0;
        }
    }
}
