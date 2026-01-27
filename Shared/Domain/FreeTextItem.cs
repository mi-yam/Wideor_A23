namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// 自由テキスト項目（Wordのテキストボックスのようなイメージ）
    /// 動画上の任意の位置に配置できるテキスト要素
    /// </summary>
    public class FreeTextItem
    {
        /// <summary>
        /// テキスト内容
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// X座標（0.0～1.0、画面左端からの比率）
        /// </summary>
        public double X { get; set; } = 0.1;

        /// <summary>
        /// Y座標（0.0～1.0、画面上端からの比率）
        /// </summary>
        public double Y { get; set; } = 0.5;

        /// <summary>
        /// フォントサイズ（nullの場合はデフォルト値を使用）
        /// </summary>
        public int? FontSize { get; set; }

        /// <summary>
        /// テキストカラー（nullの場合はデフォルト値を使用）
        /// </summary>
        public string? TextColor { get; set; }

        /// <summary>
        /// 背景色（nullの場合は半透明黒）
        /// </summary>
        public string? BackgroundColor { get; set; }

        /// <summary>
        /// 最大幅（ピクセル、nullの場合は制限なし）
        /// </summary>
        public double? MaxWidth { get; set; }

        /// <summary>
        /// 元テキストの行番号（デバッグ用）
        /// </summary>
        public int LineNumber { get; set; }
    }
}
