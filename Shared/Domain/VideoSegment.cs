using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    public class VideoSegment : INotifyPropertyChanged
    {
        private int _id;
        private double _startTime;
        private double _endTime;
        private bool _visible = true;
        private SegmentState _state = SegmentState.Stopped;
        private string _videoFilePath = string.Empty;
        private BitmapSource? _thumbnail;
        private double _speedRate = 1.0;
        private string? _title;
        private string? _subtitle;
        private List<FreeTextItem> _freeTextItems = new();

        /// <summary>セグメントの一意なID</summary>
        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>元動画内の開始時間（秒）</summary>
        public double StartTime
        {
            get => _startTime;
            set
            {
                if (SetProperty(ref _startTime, value))
                {
                    OnPropertyChanged(nameof(Duration));
                    OnPropertyChanged(nameof(EffectiveDuration));
                }
            }
        }

        /// <summary>元動画内の終了時間（秒）</summary>
        public double EndTime
        {
            get => _endTime;
            set
            {
                if (SetProperty(ref _endTime, value))
                {
                    OnPropertyChanged(nameof(Duration));
                    OnPropertyChanged(nameof(EffectiveDuration));
                }
            }
        }

        /// <summary>表示/非表示フラグ</summary>
        public bool Visible
        {
            get => _visible;
            set => SetProperty(ref _visible, value);
        }

        /// <summary>セグメントの状態</summary>
        public SegmentState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        /// <summary>元動画ファイルのパス</summary>
        public string VideoFilePath
        {
            get => _videoFilePath;
            set => SetProperty(ref _videoFilePath, value);
        }

        /// <summary>サムネイル画像（停止中に表示）</summary>
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }

        /// <summary>再生速度（1.0 = 通常速度、0.5 = 半分、2.0 = 倍速）</summary>
        public double SpeedRate
        {
            get => _speedRate;
            set
            {
                if (SetProperty(ref _speedRate, value))
                {
                    OnPropertyChanged(nameof(EffectiveDuration));
                }
            }
        }

        /// <summary>タイトル（パラグラフ装飾用）</summary>
        public string? Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>字幕（パラグラフ装飾用）</summary>
        public string? Subtitle
        {
            get => _subtitle;
            set => SetProperty(ref _subtitle, value);
        }

        /// <summary>自由テキスト項目のリスト（パラグラフ装飾用）</summary>
        public List<FreeTextItem> FreeTextItems
        {
            get => _freeTextItems;
            set => SetProperty(ref _freeTextItems, value);
        }

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

        // INotifyPropertyChanged実装
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
