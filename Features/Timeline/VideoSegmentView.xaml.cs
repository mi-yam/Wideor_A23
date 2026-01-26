using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Wideor.App.Shared.Domain;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// VideoSegmentView.xaml の相互作用ロジック
    /// 各ビデオセグメントが独立したプレイヤーを持つ
    /// 同時に1つのクリップのみ再生可能（メモリ効率のため）
    /// </summary>
    public partial class VideoSegmentView : UserControl, IDisposable
    {
        /// <summary>
        /// 現在再生中のVideoSegmentView（同時に1つのみ再生可能）
        /// </summary>
        private static VideoSegmentView? _currentlyPlayingView;
        private static readonly object _playbackLock = new object();

        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private CompositeDisposable? _disposables;
        private bool _isPlaying = false;
        private bool _isDraggingSlider = false;
        private double _totalDuration = 0;
        private bool _isDisposed = false;

        public VideoSegment Segment
        {
            get => (VideoSegment)GetValue(SegmentProperty);
            set => SetValue(SegmentProperty, value);
        }

        public static readonly DependencyProperty SegmentProperty =
            DependencyProperty.Register(nameof(Segment), typeof(VideoSegment), typeof(VideoSegmentView),
                new PropertyMetadata(null, OnSegmentChanged));

        /// <summary>
        /// クリップの高さ（FilmStripViewから制御）
        /// </summary>
        public double ClipHeight
        {
            get => (double)GetValue(ClipHeightProperty);
            set => SetValue(ClipHeightProperty, value);
        }

        public static readonly DependencyProperty ClipHeightProperty =
            DependencyProperty.Register(
                nameof(ClipHeight),
                typeof(double),
                typeof(VideoSegmentView),
                new PropertyMetadata(220.0, OnClipHeightChanged));

        private static void OnClipHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoSegmentView view && e.NewValue is double newHeight)
            {
                view.UpdateClipHeight(newHeight);
            }
        }

        private void UpdateClipHeight(double height)
        {
            if (height > 0 && MainBorder != null)
            {
                MainBorder.Height = height;
            }
        }

        // MediaPlayerプロパティは互換性のために残すが、使用しない
        public MediaPlayer? MediaPlayer
        {
            get => _mediaPlayer;
            set { } // 無視（各セグメントは独自のMediaPlayerを持つ）
        }

        public static readonly DependencyProperty MediaPlayerProperty =
            DependencyProperty.Register(
                nameof(MediaPlayer),
                typeof(MediaPlayer),
                typeof(VideoSegmentView),
                new PropertyMetadata(null));

        private static void OnSegmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoSegmentView view)
            {
                view.UpdateView();
            }
        }

        public VideoSegmentView()
        {
            InitializeComponent();
            Loaded += VideoSegmentView_Loaded;
            Unloaded += VideoSegmentView_Unloaded;
        }

        private void VideoSegmentView_Loaded(object sender, RoutedEventArgs e)
        {
            LogHelper.WriteLog(
                "VideoSegmentView.xaml.cs:Loaded",
                "VideoSegmentView loaded",
                new { 
                    hasSegment = Segment != null, 
                    segmentId = Segment?.Id ?? -1,
                    clipHeight = ClipHeight
                });
            
            // ClipHeightが設定されていない場合、デフォルト値を設定
            if (ClipHeight <= 0)
            {
                ClipHeight = 220.0;
            }
            
            UpdateView();
        }

        private void VideoSegmentView_Unloaded(object sender, RoutedEventArgs e)
        {
            // 現在再生中の参照をクリア
            lock (_playbackLock)
            {
                if (_currentlyPlayingView == this)
                {
                    _currentlyPlayingView = null;
                }
            }
            
            StopAndCleanup();
        }

        private void UpdateView()
        {
            if (Segment != null)
            {
                _totalDuration = Segment.Duration;
                TotalTimeText.Text = FormatTime(_totalDuration);
                PlaceholderDurationText.Text = FormatTime(_totalDuration);
                ThumbnailDurationText.Text = FormatTime(_totalDuration);
                
                // ファイル名を表示
                if (!string.IsNullOrEmpty(Segment.VideoFilePath))
                {
                    FileNameText.Text = System.IO.Path.GetFileName(Segment.VideoFilePath);
                }
                else
                {
                    FileNameText.Text = $"Clip {Segment.Id}";
                }
                
                // サムネイルを表示
                UpdateThumbnailDisplay();
                
                // サムネイルがない場合は非同期で読み込み
                if (Segment.Thumbnail == null && !string.IsNullOrEmpty(Segment.VideoFilePath))
                {
                    _ = LoadThumbnailAsync();
                }
                
                // 再生状態に応じてUIを更新
                _isPlaying = Segment.State == SegmentState.Playing;
                UpdatePlaybackUI();
            }
        }

        /// <summary>
        /// サムネイル表示を更新
        /// </summary>
        private void UpdateThumbnailDisplay()
        {
            if (Segment?.Thumbnail != null)
            {
                ThumbnailImage.Source = Segment.Thumbnail;
                ThumbnailImage.Visibility = Visibility.Visible;
                NoThumbnailPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ThumbnailImage.Source = null;
                ThumbnailImage.Visibility = Visibility.Collapsed;
                NoThumbnailPanel.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// サムネイルを非同期で読み込み
        /// </summary>
        private async Task LoadThumbnailAsync()
        {
            if (Segment == null || string.IsNullOrEmpty(Segment.VideoFilePath))
                return;

            try
            {
                // DIコンテナからIThumbnailProviderを取得
                IThumbnailProvider? thumbnailProvider = null;
                
                var serviceProvider = Wideor_A23.App.ServiceProvider;
                if (serviceProvider != null)
                {
                    thumbnailProvider = serviceProvider.GetService<IThumbnailProvider>();
                }

                if (thumbnailProvider == null)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:LoadThumbnailAsync",
                        "ThumbnailProvider not available from DI container",
                        new { segmentId = Segment.Id });
                    return;
                }

                // 動画の最初のフレーム（0秒位置）のサムネイルを生成
                var thumbnail = await thumbnailProvider.GenerateThumbnailAsync(
                    Segment.VideoFilePath,
                    timePosition: 0.0, // 最初のフレーム
                    width: 320,
                    height: 180,
                    cancellationToken: default);

                if (thumbnail != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (Segment != null)
                        {
                            Segment.Thumbnail = thumbnail;
                            UpdateThumbnailDisplay();
                            
                            LogHelper.WriteLog(
                                "VideoSegmentView.xaml.cs:LoadThumbnailAsync",
                                "Thumbnail loaded successfully",
                                new { segmentId = Segment.Id, videoPath = Segment.VideoFilePath });
                        }
                    });
                }
                else
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:LoadThumbnailAsync",
                        "Thumbnail generation returned null",
                        new { segmentId = Segment?.Id ?? -1, videoPath = Segment?.VideoFilePath });
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:LoadThumbnailAsync",
                    "Failed to generate thumbnail",
                    new { segmentId = Segment?.Id ?? -1, error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        private void UpdatePlaybackUI()
        {
            var icon = _isPlaying ? "⏸" : "▶";
            PlayPauseIcon.Text = icon;
            CenterPlayIcon.Text = icon;
            
            // 再生中はVideoViewを表示、それ以外はサムネイル/プレースホルダー
            if (_isPlaying && _mediaPlayer != null)
            {
                VideoPlayer.Visibility = Visibility.Visible;
                ThumbnailPlaceholder.Visibility = Visibility.Collapsed;
                CenterPlayButton.Opacity = 0.3;
            }
            else
            {
                VideoPlayer.Visibility = Visibility.Collapsed;
                ThumbnailPlaceholder.Visibility = Visibility.Visible;
                CenterPlayButton.Opacity = 1.0;
                
                // サムネイル表示を更新
                UpdateThumbnailDisplay();
            }
        }

        /// <summary>
        /// MediaPlayerを作成し、動画をロード
        /// </summary>
        private async Task<bool> InitializeAndLoadAsync()
        {
            if (Segment == null || string.IsNullOrEmpty(Segment.VideoFilePath))
                return false;

            try
            {
                // LibVLCがまだ作成されていない場合は作成
                if (_libVLC == null)
                {
                    _libVLC = new LibVLC();
                }

                // 既存のMediaPlayerをクリーンアップ
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                }

                // 新しいMediaPlayerを作成
                _mediaPlayer = new MediaPlayer(_libVLC);
                SetupMediaPlayerEvents();

                // 動画をロード
                var media = new Media(_libVLC, Segment.VideoFilePath, FromType.FromPath);
                _mediaPlayer.Media = media;

                // VideoViewにMediaPlayerを設定
                await Dispatcher.InvokeAsync(() =>
                {
                    if (VideoPlayer.IsLoaded)
                    {
                        VideoPlayer.MediaPlayer = _mediaPlayer;
                    }
                    else
                    {
                        VideoPlayer.Loaded += (s, e) =>
                        {
                            VideoPlayer.MediaPlayer = _mediaPlayer;
                        };
                    }
                }, DispatcherPriority.Loaded);

                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:InitializeAndLoadAsync",
                    "MediaPlayer initialized",
                    new { segmentId = Segment.Id, videoPath = Segment.VideoFilePath });

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:InitializeAndLoadAsync",
                    "Failed to initialize MediaPlayer",
                    new { segmentId = Segment?.Id ?? -1, error = ex.Message });
                return false;
            }
        }

        private void SetupMediaPlayerEvents()
        {
            _disposables?.Dispose();
            _disposables = new CompositeDisposable();

            if (_mediaPlayer == null) return;

            _mediaPlayer.Playing += MediaPlayer_Playing;
            _mediaPlayer.Paused += MediaPlayer_Paused;
            _mediaPlayer.Stopped += MediaPlayer_Stopped;
            _mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
            _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            _mediaPlayer.EndReached += MediaPlayer_EndReached;

            _disposables.Add(Disposable.Create(() =>
            {
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Playing -= MediaPlayer_Playing;
                    _mediaPlayer.Paused -= MediaPlayer_Paused;
                    _mediaPlayer.Stopped -= MediaPlayer_Stopped;
                    _mediaPlayer.LengthChanged -= MediaPlayer_LengthChanged;
                    _mediaPlayer.TimeChanged -= MediaPlayer_TimeChanged;
                    _mediaPlayer.EndReached -= MediaPlayer_EndReached;
                }
            }));
        }

        private void MediaPlayer_Playing(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isPlaying = true;
                if (Segment != null) Segment.State = SegmentState.Playing;
                UpdatePlaybackUI();
            });
        }

        private void MediaPlayer_Paused(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isPlaying = false;
                if (Segment != null) Segment.State = SegmentState.Stopped;
                UpdatePlaybackUI();
            });
        }

        private void MediaPlayer_Stopped(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isPlaying = false;
                if (Segment != null) Segment.State = SegmentState.Stopped;
                UpdatePlaybackUI();
                ProgressSlider.Value = 0;
                CurrentTimeText.Text = "00:00";
            });
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isPlaying = false;
                if (Segment != null) Segment.State = SegmentState.Stopped;
                UpdatePlaybackUI();
                ProgressSlider.Value = 0;
                CurrentTimeText.Text = "00:00";
                
                // 現在再生中の参照をクリア
                lock (_playbackLock)
                {
                    if (_currentlyPlayingView == this)
                    {
                        _currentlyPlayingView = null;
                    }
                }
                
                // メモリを解放するためにMediaPlayerをクリーンアップ
                StopAndCleanup();
            });
        }

        private void MediaPlayer_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _totalDuration = e.Length / 1000.0;
                TotalTimeText.Text = FormatTime(_totalDuration);
                PlaceholderDurationText.Text = FormatTime(_totalDuration);
            });
        }

        private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!_isDraggingSlider && _totalDuration > 0)
                {
                    var currentTime = e.Time / 1000.0;
                    var progress = (currentTime / _totalDuration) * 100;
                    ProgressSlider.Value = Math.Min(100, Math.Max(0, progress));
                    CurrentTimeText.Text = FormatTime(currentTime);
                }
            });
        }

        /// <summary>
        /// 再生を停止し、リソースをクリーンアップ
        /// </summary>
        private void StopAndCleanup()
        {
            _disposables?.Dispose();
            _disposables = null;

            if (_mediaPlayer != null)
            {
                try
                {
                    _mediaPlayer.Stop();
                    
                    // VideoViewからMediaPlayerを解除
                    Dispatcher.Invoke(() =>
                    {
                        if (VideoPlayer != null)
                        {
                            VideoPlayer.MediaPlayer = null;
                        }
                    });
                    
                    _mediaPlayer.Dispose();
                }
                catch { }
                _mediaPlayer = null;
            }

            _isPlaying = false;
            
            Dispatcher.InvokeAsync(() =>
            {
                UpdatePlaybackUI();
            });
        }

        private string FormatTime(double seconds)
        {
            var timeSpan = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        // --- イベントハンドラ ---

        private async void OnPlayPauseClicked(object sender, RoutedEventArgs e)
        {
            if (Segment == null) return;

            LogHelper.WriteLog(
                "VideoSegmentView.xaml.cs:OnPlayPauseClicked",
                "Play/Pause button clicked",
                new { segmentId = Segment.Id, isPlaying = _isPlaying });

            if (_isPlaying)
            {
                // 再生中 → 一時停止（サムネイル状態に戻す）
                StopAndCleanup();
                ProgressSlider.Value = 0;
                CurrentTimeText.Text = "00:00";
                
                lock (_playbackLock)
                {
                    if (_currentlyPlayingView == this)
                    {
                        _currentlyPlayingView = null;
                    }
                }
            }
            else
            {
                // 停止中 → 再生開始
                // 他のクリップが再生中の場合は先に停止
                VideoSegmentView? previousView = null;
                
                lock (_playbackLock)
                {
                    if (_currentlyPlayingView != null && _currentlyPlayingView != this)
                    {
                        previousView = _currentlyPlayingView;
                        _currentlyPlayingView = null;
                    }
                    
                    // このクリップを現在再生中として設定
                    _currentlyPlayingView = this;
                }
                
                // ロック外で前のクリップを停止（UIスレッドで実行）
                if (previousView != null)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:OnPlayPauseClicked",
                        "Stopping currently playing clip",
                        new { 
                            currentSegmentId = previousView.Segment?.Id ?? -1, 
                            newSegmentId = Segment.Id 
                        });
                    
                    previousView.Dispatcher.Invoke(() =>
                    {
                        previousView.StopAndCleanup();
                        previousView.ProgressSlider.Value = 0;
                        previousView.CurrentTimeText.Text = "00:00";
                    });
                }
                
                // MediaPlayerがない場合は初期化
                if (_mediaPlayer == null)
                {
                    var initialized = await InitializeAndLoadAsync();
                    if (!initialized)
                    {
                        lock (_playbackLock)
                        {
                            if (_currentlyPlayingView == this)
                            {
                                _currentlyPlayingView = null;
                            }
                        }
                        return;
                    }
                }

                // 最初から再生開始
                _mediaPlayer?.Play();
            }
        }

        private void OnStopClicked(object sender, RoutedEventArgs e)
        {
            if (Segment == null) return;

            LogHelper.WriteLog(
                "VideoSegmentView.xaml.cs:OnStopClicked",
                "Stop button clicked",
                new { segmentId = Segment.Id });

            // 現在再生中の参照をクリア
            lock (_playbackLock)
            {
                if (_currentlyPlayingView == this)
                {
                    _currentlyPlayingView = null;
                }
            }

            StopAndCleanup();
            ProgressSlider.Value = 0;
            CurrentTimeText.Text = "00:00";
        }

        private void OnProgressSliderMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void OnProgressSliderMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            
            if (_mediaPlayer != null && _totalDuration > 0)
            {
                var seekPosition = (ProgressSlider.Value / 100.0) * _totalDuration;
                _mediaPlayer.Time = (long)(seekPosition * 1000);
            }
        }

        private void OnProgressSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider && _totalDuration > 0)
            {
                var currentTime = (e.NewValue / 100.0) * _totalDuration;
                CurrentTimeText.Text = FormatTime(currentTime);
            }
        }

        private void OnVolumeSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = (int)e.NewValue;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 現在再生中の参照をクリア
            lock (_playbackLock)
            {
                if (_currentlyPlayingView == this)
                {
                    _currentlyPlayingView = null;
                }
            }

            StopAndCleanup();

            if (_libVLC != null)
            {
                _libVLC.Dispose();
                _libVLC = null;
            }
        }

        /// <summary>
        /// 現在再生中のクリップがあるかどうか
        /// </summary>
        public static bool IsAnyClipPlaying
        {
            get
            {
                lock (_playbackLock)
                {
                    return _currentlyPlayingView != null;
                }
            }
        }

        /// <summary>
        /// 現在再生中のクリップを停止
        /// </summary>
        public static void StopCurrentlyPlayingClip()
        {
            VideoSegmentView? viewToStop = null;
            
            lock (_playbackLock)
            {
                if (_currentlyPlayingView != null)
                {
                    viewToStop = _currentlyPlayingView;
                    _currentlyPlayingView = null;
                }
            }
            
            if (viewToStop != null)
            {
                viewToStop.Dispatcher.Invoke(() =>
                {
                    viewToStop.StopAndCleanup();
                    viewToStop.ProgressSlider.Value = 0;
                    viewToStop.CurrentTimeText.Text = "00:00";
                });
            }
        }
    }
}
