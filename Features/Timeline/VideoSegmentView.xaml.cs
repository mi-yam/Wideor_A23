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
        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
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
                // 動画のアスペクト比を考慮して高さを調整
                var adjustedHeight = CalculateOptimalHeight(height);
                MainBorder.Height = adjustedHeight;
                
                // MainBorderの最大高さも設定（親要素からのはみ出しを防止）
                // ただし、最小高さ（150px）は保証する
                MainBorder.MinHeight = 150;
            }
        }

        /// <summary>
        /// 動画のアスペクト比を考慮して最適な高さを計算
        /// フィルムエリアの幅に基づいて、動画がはみ出ないように高さを計算
        /// </summary>
        private double CalculateOptimalHeight(double baseHeight)
        {
            // 親要素（FilmStripView）の幅を取得
            var availableWidth = GetParentWidth();
            
            // サムネイル画像からアスペクト比を取得
            if (Segment?.Thumbnail != null)
            {
                var thumbnail = Segment.Thumbnail;
                
                // BitmapSourceがFreezeされている場合、PixelWidth/PixelHeightを使用（スレッドセーフ）
                // Freezeされていない場合でも、PixelWidth/PixelHeightは使用可能
                double thumbnailWidth = 0;
                double thumbnailHeight = 0;
                
                try
                {
                    // PixelWidth/PixelHeightはスレッドセーフ（Freeze後は確実にスレッドセーフ）
                    thumbnailWidth = thumbnail.PixelWidth;
                    thumbnailHeight = thumbnail.PixelHeight;
                }
                catch
                {
                    // エラーが発生した場合は、ベースの高さを使用
                    return Math.Max(150, baseHeight);
                }
                
                if (thumbnailWidth > 0 && thumbnailHeight > 0)
                {
                    var aspectRatio = thumbnailHeight / thumbnailWidth;
                    
                    if (availableWidth > 0)
                    {
                        // 利用可能な幅に基づいて動画エリアの高さを計算
                        var videoHeight = availableWidth * aspectRatio;
                        // ヘッダー（約25px）とコントロールバー（約35px）の高さを追加
                        var totalHeight = videoHeight + 60;
                        // 最小高さ（150px）を保証
                        return Math.Max(150, totalHeight);
                    }
                }
            }
            
            // アスペクト比が取得できない場合は、ベースの高さを使用（最小150px）
            return Math.Max(150, baseHeight);
        }

        /// <summary>
        /// 親要素（FilmStripView）の幅を取得
        /// </summary>
        private double GetParentWidth()
        {
            // まずActualWidthを試す（これが最も正確）
            if (ActualWidth > 0)
            {
                // スクロールバーの幅（約20px）とボーダー分を考慮
                return Math.Max(260, ActualWidth - 20);
            }
            
            // 親要素から取得を試みる
            var parent = Parent;
            while (parent != null)
            {
                if (parent is FilmStripView filmStrip && filmStrip.ActualWidth > 0)
                {
                    // FilmStripViewの幅からスクロールバー分を差し引く
                    return Math.Max(260, filmStrip.ActualWidth - 20);
                }
                if (parent is FrameworkElement fe && fe.ActualWidth > 0)
                {
                    return Math.Max(260, fe.ActualWidth - 20);
                }
                parent = LogicalTreeHelper.GetParent(parent) ?? System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            
            // デフォルト幅（260px - 最小サイズ）
            return 260.0;
        }

        // MediaPlayerプロパティは互換性のために残すが、使用しない
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer
        {
            get => _mediaPlayer;
            set { } // 無視（各セグメントは独自のMediaPlayerを持つ）
        }

        public static readonly DependencyProperty MediaPlayerProperty =
            DependencyProperty.Register(
                nameof(MediaPlayer),
                typeof(LibVLCSharp.Shared.MediaPlayer),
                typeof(VideoSegmentView),
                new PropertyMetadata(default(LibVLCSharp.Shared.MediaPlayer)));

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
            SizeChanged += VideoSegmentView_SizeChanged;
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

        private void VideoSegmentView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 幅が変更された場合、高さを再計算
            if (e.WidthChanged && ClipHeight > 0)
            {
                UpdateClipHeight(ClipHeight);
            }
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
                
                // サムネイルが読み込まれたら、高さを再計算（UIスレッドで実行）
                if (ClipHeight > 0)
                {
                    if (Dispatcher.CheckAccess())
                    {
                        UpdateClipHeight(ClipHeight);
                    }
                    else
                    {
                        Dispatcher.Invoke(() => UpdateClipHeight(ClipHeight));
                    }
                }
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
                // ThumbnailProvider内でFreeze()が呼び出されるため、返されるBitmapSourceは既にスレッドセーフ
                var thumbnail = await thumbnailProvider.GenerateThumbnailAsync(
                    Segment.VideoFilePath,
                    timePosition: 0.0, // 最初のフレーム
                    width: 320,
                    height: 180,
                    cancellationToken: default).ConfigureAwait(false);

                if (thumbnail != null)
                {
                    // BitmapSourceがFreezeされているか確認（ThumbnailProviderで既にFreezeされているはず）
                    // Freezeされていない場合のみFreeze()を試みる
                    if (!thumbnail.IsFrozen)
                    {
                        try
                        {
                            // CanFreeze()が可能な場合のみFreeze()を呼び出す
                            if (thumbnail.CanFreeze)
                            {
                                thumbnail.Freeze();
                            }
                        }
                        catch
                        {
                            // Freeze()に失敗した場合は、そのまま使用（PixelWidth/PixelHeightは使用可能）
                        }
                    }
                    
                    // サイズ情報を取得（Freeze後なので、どのスレッドからでもアクセス可能）
                    int thumbnailWidth = 0;
                    int thumbnailHeight = 0;
                    try
                    {
                        thumbnailWidth = thumbnail.PixelWidth;
                        thumbnailHeight = thumbnail.PixelHeight;
                    }
                    catch
                    {
                        // サイズ取得に失敗した場合は、デフォルト値を使用
                        thumbnailWidth = 320;
                        thumbnailHeight = 180;
                    }
                    
                    // UIスレッドでサムネイルを設定
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (Segment != null)
                        {
                            Segment.Thumbnail = thumbnail;
                            UpdateThumbnailDisplay();
                            
                            LogHelper.WriteLog(
                                "VideoSegmentView.xaml.cs:LoadThumbnailAsync",
                                "Thumbnail loaded successfully",
                                new { segmentId = Segment.Id, videoPath = Segment.VideoFilePath, width = thumbnailWidth, height = thumbnailHeight });
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
                    // イベントハンドラを先に解除
                    _disposables?.Dispose();
                    _disposables = null;
                    
                    var oldPlayer = _mediaPlayer;
                    _mediaPlayer = null;
                    
                    // UIスレッドでVideoViewとの関連付けを解除
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (VideoPlayer != null && !_isDisposed)
                        {
                            VideoPlayer.MediaPlayer = null;
                        }
                    }, DispatcherPriority.Normal);
                    
                    // MediaPlayerを停止
                    try
                    {
                        oldPlayer.Stop();
                    }
                    catch { }
                    
                    // 少し待機してから非同期で破棄（VideoViewの内部処理完了を待つ）
                    await Task.Delay(50);
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        try
                        {
                            oldPlayer.Dispose();
                        }
                        catch { }
                    });
                }

                // 新しいMediaPlayerを作成
                _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                SetupMediaPlayerEvents();

                // 動画をロード
                var media = new Media(_libVLC, Segment.VideoFilePath, FromType.FromPath);
                _mediaPlayer.Media = media;

                // VideoViewにMediaPlayerを設定
                await Dispatcher.InvokeAsync(() =>
                {
                    if (VideoPlayer != null && !_isDisposed && VideoPlayer.IsLoaded)
                    {
                        VideoPlayer.MediaPlayer = _mediaPlayer;
                    }
                    else if (VideoPlayer != null && !_isDisposed)
                    {
                        VideoPlayer.Loaded += (s, e) =>
                        {
                            if (!_isDisposed)
                            {
                                VideoPlayer.MediaPlayer = _mediaPlayer;
                            }
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
                    new { segmentId = Segment?.Id ?? -1, error = ex.Message, stackTrace = ex.StackTrace });
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
            // 注意: EndReachedイベントはネイティブスレッドから発火されるため、
            // イベントハンドラ内でMediaPlayerを直接破棄するとAccessViolationExceptionが発生する。
            // そのため、破棄は遅延させて非同期で実行する。
            
            Dispatcher.BeginInvoke(() =>
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
                
                // メモリを解放するためにMediaPlayerをクリーンアップ（遅延実行）
                // イベント処理が完了してから破棄するため、少し遅延させる
                ScheduleCleanup();
            }, DispatcherPriority.Background);
        }
        
        /// <summary>
        /// MediaPlayerのクリーンアップを遅延実行でスケジュール
        /// イベントハンドラ内から安全に呼び出せる
        /// </summary>
        private async void ScheduleCleanup()
        {
            // イベント処理が完全に完了するまで少し待機
            await Task.Delay(100);
            
            // UIスレッドでクリーンアップを実行
            await Dispatcher.InvokeAsync(() =>
            {
                StopAndCleanupSafe();
            }, DispatcherPriority.Background);
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
        /// 再生を停止し、リソースをクリーンアップ（安全版 - UI状態もリセット）
        /// UIスレッドで呼び出すこと、またはUIスレッド外から呼び出すと自動的にUIスレッドに切り替え
        /// </summary>
        private void StopAndCleanupSafe()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => StopAndCleanupSafe(), DispatcherPriority.Normal);
                return;
            }
            
            // イベントハンドラを先に解除
            _disposables?.Dispose();
            _disposables = null;

            var playerToDispose = _mediaPlayer;
            _mediaPlayer = null;
            _isPlaying = false;

            if (playerToDispose != null)
            {
                try
                {
                    // VideoViewからMediaPlayerを解除
                    if (VideoPlayer != null && !_isDisposed)
                    {
                        VideoPlayer.MediaPlayer = null;
                    }
                    
                    // MediaPlayerを停止（既に停止している場合は何もしない）
                    try
                    {
                        playerToDispose.Stop();
                    }
                    catch { }
                    
                    // 破棄は非同期で遅延実行（VideoViewの内部処理完了を待つ）
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        try
                        {
                            playerToDispose.Dispose();
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLog(
                                "VideoSegmentView.xaml.cs:StopAndCleanupSafe",
                                "Error disposing MediaPlayer",
                                new { error = ex.Message });
                        }
                    });
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:StopAndCleanupSafe",
                        "Error during cleanup",
                        new { error = ex.Message, stackTrace = ex.StackTrace });
                }
            }

            // UI状態をリセット
            UpdatePlaybackUI();
            ProgressSlider.Value = 0;
            CurrentTimeText.Text = "00:00";
        }
        
        /// <summary>
        /// 再生を停止し、リソースをクリーンアップ（同期版 - ユーザー操作用）
        /// </summary>
        private void StopAndCleanup()
        {
            if (!Dispatcher.CheckAccess())
            {
                // UIスレッドでない場合はBeginInvokeで非同期実行（デッドロック回避）
                Dispatcher.BeginInvoke(() => StopAndCleanup(), DispatcherPriority.Normal);
                return;
            }
            
            // イベントハンドラを先に解除
            _disposables?.Dispose();
            _disposables = null;

            var playerToDispose = _mediaPlayer;
            _mediaPlayer = null;
            _isPlaying = false;

            if (playerToDispose != null)
            {
                try
                {
                    // VideoViewからMediaPlayerを解除
                    if (VideoPlayer != null && !_isDisposed)
                    {
                        VideoPlayer.MediaPlayer = null;
                    }
                    
                    // MediaPlayerを停止
                    try
                    {
                        playerToDispose.Stop();
                    }
                    catch { }
                    
                    // 少し待機してから破棄（非同期）
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        try
                        {
                            playerToDispose.Dispose();
                        }
                        catch { }
                    });
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:StopAndCleanup",
                        "Error during cleanup",
                        new { error = ex.Message });
                }
            }

            UpdatePlaybackUI();
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
                // ロック内で参照をクリアしてからロック外でStopAndCleanupを呼び出す
                bool shouldClearReference = false;
                lock (_playbackLock)
                {
                    if (_currentlyPlayingView == this)
                    {
                        _currentlyPlayingView = null;
                        shouldClearReference = true;
                    }
                }
                
                // ロック外でStopAndCleanupを実行（デッドロック回避）
                StopAndCleanup();
                ProgressSlider.Value = 0;
                CurrentTimeText.Text = "00:00";
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
                    }
                    
                    // このクリップを現在再生中として設定
                    _currentlyPlayingView = this;
                }
                
                // ロック外で前のクリップを停止
                if (previousView != null)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:OnPlayPauseClicked",
                        "Stopping currently playing clip",
                        new { 
                            currentSegmentId = previousView.Segment?.Id ?? -1, 
                            newSegmentId = Segment.Id 
                        });
                    
                    // 同じUIスレッド上で直接実行（Dispatcherを使わずにデッドロックを回避）
                    // ただし、previousViewが同じUIスレッドにある場合のみ
                    if (previousView.Dispatcher == Dispatcher)
                    {
                        previousView.StopAndCleanupSafe();
                    }
                    else
                    {
                        // 異なるスレッドの場合は非同期で実行
                        previousView.Dispatcher.BeginInvoke(() =>
                        {
                            previousView.StopAndCleanupSafe();
                        }, DispatcherPriority.Normal);
                    }
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

            // イベントハンドラを先に解除
            _disposables?.Dispose();
            _disposables = null;
            
            // MediaPlayerを安全に破棄
            var playerToDispose = _mediaPlayer;
            _mediaPlayer = null;
            
            if (playerToDispose != null)
            {
                // UIスレッドでVideoViewとの関連付けを解除
                if (Dispatcher.CheckAccess())
                {
                    if (VideoPlayer != null)
                    {
                        VideoPlayer.MediaPlayer = null;
                    }
                }
                else
                {
                    try
                    {
                        // UIスレッドでない場合はBeginInvokeで非同期実行（デッドロック回避）
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (VideoPlayer != null)
                            {
                                VideoPlayer.MediaPlayer = null;
                            }
                        }, DispatcherPriority.Normal);
                    }
                    catch { }
                }
                
                // MediaPlayerを停止して破棄（非同期）
                try
                {
                    playerToDispose.Stop();
                }
                catch { }
                
                // 破棄は非同期で遅延実行
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    try
                    {
                        playerToDispose.Dispose();
                    }
                    catch { }
                });
            }
            
            // LibVLCも非同期で破棄
            var libVlcToDispose = _libVLC;
            _libVLC = null;
            
            if (libVlcToDispose != null)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(200);
                    try
                    {
                        libVlcToDispose.Dispose();
                    }
                    catch { }
                });
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
                // BeginInvokeを使用してデッドロックを回避（待機しない）
                viewToStop.Dispatcher.BeginInvoke(() =>
                {
                    viewToStop.StopAndCleanupSafe();
                }, DispatcherPriority.Normal);
            }
        }
    }
}
