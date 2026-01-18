using System;
using System.Globalization;
using System.Windows.Data;

namespace Wideor.App.Features.Player
{
    /// <summary>
    /// 再生状態（bool）を再生/一時停止アイコン（文字列）に変換するコンバーター
    /// </summary>
    public class BooleanToPlayPauseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPlaying)
            {
                return isPlaying ? "⏸" : "▶";
            }
            return "▶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
