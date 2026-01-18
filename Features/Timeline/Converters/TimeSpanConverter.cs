using System;
using System.Globalization;
using System.Windows.Data;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// 秒（double）を時間表示文字列に変換するコンバーター（Timeline機能専用）
    /// </summary>
    public class TimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double seconds)
            {
                var timeSpan = TimeSpan.FromSeconds(seconds);
                if (timeSpan.TotalHours >= 1)
                {
                    return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                }
                else
                {
                    return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                }
            }
            return "00:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
