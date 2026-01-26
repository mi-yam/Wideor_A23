namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// シーンブロックを表す不変データモデル。
    /// 動画編集における基本的なシーン単位の情報を保持します。
    /// </summary>
    public record SceneBlock
    {
        /// <summary>
        /// シーンブロックの一意な識別子
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// シーンの開始時間（秒）
        /// </summary>
        public required double StartTime { get; init; }

        /// <summary>
        /// シーンの終了時間（秒）
        /// </summary>
        public required double EndTime { get; init; }

        /// <summary>
        /// シーンの持続時間（秒）
        /// </summary>
        public double Duration => EndTime - StartTime;

        /// <summary>
        /// シーンのタイトル（# で始まる行）
        /// </summary>
        public string? Title { get; init; }

        /// <summary>
        /// 字幕テキスト（> で始まる行）
        /// </summary>
        public string? Subtitle { get; init; }

        /// <summary>
        /// コンテンツテキスト（セパレータ以下のすべてのテキスト）
        /// </summary>
        public string? ContentText { get; init; }

        /// <summary>
        /// テキストエディタ上の行番号
        /// </summary>
        public int LineNumber { get; init; }

        /// <summary>
        /// 関連するメディアファイルのパス
        /// </summary>
        public string? MediaFilePath { get; init; }

        /// <summary>
        /// シーンの種類（例: "video", "audio", "image"）
        /// </summary>
        public string? SceneType { get; init; }

        /// <summary>
        /// メタデータ（キー・バリューペア）
        /// </summary>
        public Dictionary<string, string> Metadata { get; init; } = new();

        /// <summary>
        /// 指定された時間がこのシーンブロックの範囲内かどうかを判定します。
        /// </summary>
        public bool ContainsTime(double time)
        {
            return time >= StartTime && time <= EndTime;
        }

        /// <summary>
        /// このシーンブロックが他のシーンブロックと重複しているかどうかを判定します。
        /// </summary>
        public bool OverlapsWith(SceneBlock other)
        {
            return StartTime < other.EndTime && EndTime > other.StartTime;
        }
    }
}
