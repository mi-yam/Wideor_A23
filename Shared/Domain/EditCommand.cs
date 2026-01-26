using System;

namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// コマンドの種類
    /// </summary>
    public enum CommandType
    {
        /// <summary>動画ファイルを読み込み</summary>
        Load,
        /// <summary>指定位置でセグメントを分割</summary>
        Cut,
        /// <summary>指定範囲を非表示</summary>
        Hide,
        /// <summary>指定範囲を表示</summary>
        Show,
        /// <summary>指定範囲を削除</summary>
        Delete,
        /// <summary>隣接するセグメントを結合</summary>
        Merge,
        /// <summary>再生速度を変更</summary>
        Speed
    }

    /// <summary>
    /// 編集コマンド
    /// </summary>
    public class EditCommand
    {
        /// <summary>コマンドの種類</summary>
        public CommandType Type { get; set; }

        /// <summary>CUT用: 分割位置（秒）</summary>
        public double? Time { get; set; }

        /// <summary>HIDE/SHOW/DELETE/MERGE用: 開始時間（秒）</summary>
        public double? StartTime { get; set; }

        /// <summary>HIDE/SHOW/DELETE/MERGE用: 終了時間（秒）</summary>
        public double? EndTime { get; set; }

        /// <summary>LOAD用: 動画ファイルパス</summary>
        public string? FilePath { get; set; }

        /// <summary>SPEED用: 再生速度（1.0 = 通常速度）</summary>
        public double? SpeedRate { get; set; }

        /// <summary>テキストエディタ上の行番号</summary>
        public int LineNumber { get; set; }

        /// <summary>
        /// コマンドを文字列に変換
        /// </summary>
        public override string ToString()
        {
            return Type switch
            {
                CommandType.Load => $"LOAD {FilePath}",
                CommandType.Cut => $"CUT {FormatTime(Time ?? 0)}",
                CommandType.Hide => $"HIDE {FormatTime(StartTime ?? 0)} {FormatTime(EndTime ?? 0)}",
                CommandType.Show => $"SHOW {FormatTime(StartTime ?? 0)} {FormatTime(EndTime ?? 0)}",
                CommandType.Delete => $"DELETE {FormatTime(StartTime ?? 0)} {FormatTime(EndTime ?? 0)}",
                CommandType.Merge => $"MERGE {FormatTime(StartTime ?? 0)} {FormatTime(EndTime ?? 0)}",
                CommandType.Speed => $"SPEED {SpeedRate:F2}x {FormatTime(StartTime ?? 0)} {FormatTime(EndTime ?? 0)}",
                _ => base.ToString() ?? string.Empty
            };
        }

        /// <summary>
        /// 時間をHH:MM:SS.mmm形式にフォーマット
        /// </summary>
        private static string FormatTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }
    }
}
