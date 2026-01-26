using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wideor.App.Features.Timeline.Converters
{
    /// <summary>
    /// boolをVisibilityに変換するコンバーター（反転）
    /// true → Collapsed
    /// false → Visible
    /// 非表示セグメントのグレーアウト表示等に使用
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// boolをVisibilityに変換（反転）
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }

            // デフォルトは非表示
            return Visibility.Collapsed;
        }

        /// <summary>
        /// VisibilityをboolsIに逆変換
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }

            return true;
        }
    }
}
