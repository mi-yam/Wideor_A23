using System.Windows.Media.Imaging;

namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// セグメントの状態
    /// </summary>
    public enum SegmentState
    {
        /// <summary>停止中（最初のフレームを表示）</summary>
        Stopped,
        /// <summary>再生中</summary>
        Playing,
        /// <summary>非表示（灰色）</summary>
        Hidden
    }

    /// <summary>
    /// 動画セグメント
    /// 動画の一部分を表し、表示/非表示、再生速度などの情報を持つ
    /// </summary>
    public class VideoSegment
    {
        /// <summary>セグメントの一意なID</summary>
        public int Id { get; set; }

        /// <summary>元動画内の開始時間（秒）</summary>
        public double StartTime { get; set; }

        /// <summary>元動画内の終了時間（秒）</summary>
        public double EndTime { get; set; }

        /// <summary>表示/非表示フラグ</summary>
        public bool Visible { get; set; } = true;

        /// <summary>セグメントの状態</summary>
        public SegmentState State { get; set; } = SegmentState.Stopped;

        /// <summary>元動画ファイルのパス</summary>
        public string VideoFilePath { get; set; } = string.Empty;

        /// <summary>サムネイル画像（停止中に表示）</summary>
        public BitmapSource? Thumbnail { get; set; }

        /// <summary>再生速度（1.0 = 通常速度、0.5 = 半分、2.0 = 倍速）</summary>
        public double SpeedRate { get; set; } = 1.0;

        /// <summary>タイトル（パラグラフ装飾用）</summary>
        public string? Title { get; set; }

        /// <summary>字幕（パラグラフ装飾用）</summary>
        public string? Subtitle { get; set; }

        /// <summary>セグメントの長さ（秒）</summary>
        public double Duration => EndTime - StartTime;

        /// <summary>再生速度を考慮した実効長（秒）</summary>
        public double EffectiveDuration => Duration / SpeedRate;

        /// <summary>
        /// 指定した時間がこのセグメント内に含まれるかどうか
        /// </summary>
        public bool ContainsTime(double time)
        {
            return time >= StartTime && time <= EndTime;
        }

        /// <summary>
        /// このセグメントが別のセグメントと重なっているかどうか
        /// </summary>
        public bool OverlapsWith(VideoSegment other)
        {
            return StartTime < other.EndTime && EndTime > other.StartTime;
        }

        /// <summary>
        /// このセグメントが別のセグメントと隣接しているかどうか
        /// </summary>
        public bool IsAdjacentTo(VideoSegment other, double tolerance = 0.001)
        {
            return System.Math.Abs(EndTime - other.StartTime) < tolerance ||
                   System.Math.Abs(other.EndTime - StartTime) < tolerance;
        }
    }
}
