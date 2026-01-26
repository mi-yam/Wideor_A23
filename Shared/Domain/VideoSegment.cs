using System.Windows.Media.Imaging;

namespace Wideor.App.Shared.Domain
{
    public enum SegmentState
    {
        Stopped,    // 停止中（最初のフレームを表示）
        Playing,    // 再生中
        Hidden      // 非表示（灰色）
    }

    public class VideoSegment
    {
        public int Id { get; set; }
        public double StartTime { get; set; }      // 元動画内の開始時間（秒）
        public double EndTime { get; set; }        // 元動画内の終了時間（秒）
        public bool Visible { get; set; }          // 表示/非表示フラグ
        public SegmentState State { get; set; }    // Stopped, Playing, Hidden
        public string VideoFilePath { get; set; }  // 元動画ファイルのパス
        
        /// <summary>
        /// サムネイル画像（停止中に表示）
        /// </summary>
        public BitmapSource? Thumbnail { get; set; }
        
        public double Duration => EndTime - StartTime;
        
        public bool ContainsTime(double time)
        {
            return time >= StartTime && time <= EndTime;
        }
    }
}
