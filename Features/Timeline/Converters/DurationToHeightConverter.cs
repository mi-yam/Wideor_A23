using System;
using System.Globalization;
using System.Windows.Data;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// 動画セグメントのDuration（秒）を高さ（ピクセル）に変換するコンバーター
    /// 固定スケール: 1秒 = 100ピクセル
    /// </summary>
    public class DurationToHeightConverter : IValueConverter
    {
        private const double PixelsPerSecond = 100.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double duration && duration > 0)
            {
                return duration * PixelsPerSecond;
            }
            return 100.0; // デフォルト高さ
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double height && height > 0)
            {
                return height / PixelsPerSecond;
            }
            return 1.0; // デフォルトDuration
        }
    }
}
