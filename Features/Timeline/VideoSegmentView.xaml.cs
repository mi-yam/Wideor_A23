using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;
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

        // LibVLCはApp.SharedLibVLCを使用（公式ベストプラクティス: "Only create one LibVLC instance at all times"）
        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
        private CompositeDisposable? _disposables;
        private bool _isPlaying = false;
        private bool _isDraggingSlider = false;
        private double _totalDuration = 0;
        private bool _isDisposed = false;
        private long? _pendingSeekTimeMs = null;  // 再生開始後にシークする位置（ミリ秒）
        private int _cachedSegmentId = -1;  // ネイティブスレッドからアクセス用のセグメントIDキャッシュ
        private double _cachedSegmentEndTime = double.MaxValue;  // ネイティブスレッドからアクセス用のセグメント終了時間キャッシュ
        private TimelineViewModel? _timelineViewModel;  // 再生位置の通知用

        public VideoSegment Segment
        {
            get => (VideoSegment)GetValue(SegmentProperty);
            set => SetValue(SegmentProperty, value);
        }

        public static readonly DependencyProperty SegmentProperty =
            DependencyProperty.Register(nameof(Segment), typeof(VideoSegment), typeof(VideoSegmentView),
                new PropertyMetadata(null, OnSegmentChanged));

        /// <summary>
        /// タイトル（パラグラフ装飾用）
        /// </summary>
        public string? Title
        {
            get => (string?)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(VideoSegmentView),
                new PropertyMetadata(null, OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoSegmentView view)
            {
                view.UpdateTitleOverlay();
            }
        }

        /// <summary>
        /// 字幕（パラグラフ装飾用）
        /// </summary>
        public string? Subtitle
        {
            get => (string?)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(VideoSegmentView),
                new PropertyMetadata(null, OnSubtitleChanged));

        private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoSegmentView view)
            {
                view.UpdateSubtitleOverlay();
            }
        }

        /// <summary>
        /// 自由テキスト項目（パラグラフ装飾用）
        /// </summary>
        public List<FreeTextItem>? FreeTextItems
        {
            get => (List<FreeTextItem>?)GetValue(FreeTextItemsProperty);
            set => SetValue(FreeTextItemsProperty, value);
        }

        public static readonly DependencyProperty FreeTextItemsProperty =
            DependencyProperty.Register(nameof(FreeTextItems), typeof(List<FreeTextItem>), typeof(VideoSegmentView),
                new PropertyMetadata(null, OnFreeTextItemsChanged));

        private static void OnFreeTextItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoSegmentView view)
            {
                view.UpdateFreeTextOverlay();
            }
        }

        /// <summary>
        /// プロジェクト設定（テロップ位置設定を使用）
        /// </summary>
        public ProjectConfig? ProjectConfig
        {
            get => (ProjectConfig?)GetValue(ProjectConfigProperty);
            set => SetValue(ProjectConfigProperty, value);
        }

        public static readonly DependencyProperty ProjectConfigProperty =
            DependencyProperty.Register(nameof(ProjectConfig), typeof(ProjectConfig), typeof(VideoSegmentView),
                new PropertyMetadata(null, OnProjectConfigChanged));

        private static void OnProjectConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoSegmentView view && view._isPlaying)
            {
                // 再生中の場合のみ、設定変更時にオーバーレイを更新
                view.UpdateTitleOverlay();
                view.UpdateSubtitleOverlay();
                view.UpdateFreeTextOverlay();
            }
        }

        /// <summary>
        /// タイトルオーバーレイを更新
        /// </summary>
        private void UpdateTitleOverlay()
        {
            if (TitleOverlayPopup == null || TitleText == null || VideoPlayer == null)
                return;

            if (!string.IsNullOrWhiteSpace(Title))
            {
                TitleText.Text = Title;
                
                // ProjectConfigから位置設定を取得（なければデフォルト値）
                var posX = ProjectConfig?.TitlePositionX ?? 0.05;
                var posY = ProjectConfig?.TitlePositionY ?? 0.05;
                var fontSize = ProjectConfig?.TitleFontSize ?? 32;
                
                TitleText.FontSize = fontSize;
                
                // VideoPlayerのサイズを基準に位置を計算
                var videoWidth = VideoPlayer.ActualWidth > 0 ? VideoPlayer.ActualWidth : 320;
                var videoHeight = VideoPlayer.ActualHeight > 0 ? VideoPlayer.ActualHeight : 180;
                
                // Popup位置を設定（VideoPlayerからの相対位置）
                TitleOverlayPopup.HorizontalOffset = posX * videoWidth;
                TitleOverlayPopup.VerticalOffset = posY * videoHeight;
                TitleOverlayPopup.IsOpen = true;
                
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:UpdateTitleOverlay",
                    "Title overlay shown",
                    new { title = Title, posX = posX, posY = posY, fontSize = fontSize, segmentId = Segment?.Id ?? -1 });
            }
            else
            {
                TitleOverlayPopup.IsOpen = false;
            }
        }

        /// <summary>
        /// 字幕オーバーレイを更新
        /// </summary>
        private void UpdateSubtitleOverlay()
        {
            if (SubtitleOverlayPopup == null || SubtitleText == null || VideoPlayer == null)
                return;

            if (!string.IsNullOrWhiteSpace(Subtitle))
            {
                SubtitleText.Text = Subtitle;
                
                // ProjectConfigから位置設定を取得（なければデフォルト値）
                var posY = ProjectConfig?.SubtitlePositionY ?? 0.85;
                var fontSize = ProjectConfig?.SubtitleFontSize ?? 24;
                
                SubtitleText.FontSize = fontSize;
                
                // VideoPlayerのサイズを基準に位置を計算
                var videoWidth = VideoPlayer.ActualWidth > 0 ? VideoPlayer.ActualWidth : 320;
                var videoHeight = VideoPlayer.ActualHeight > 0 ? VideoPlayer.ActualHeight : 180;
                
                // 中央揃えのため、横位置は動画の中央から字幕の半分を引く
                // MaxWidth=500なので、おおよそ中央に配置
                SubtitleOverlayPopup.HorizontalOffset = (videoWidth - 500) / 2;
                SubtitleOverlayPopup.VerticalOffset = posY * videoHeight;
                SubtitleOverlayPopup.IsOpen = true;
                
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:UpdateSubtitleOverlay",
                    "Subtitle overlay shown",
                    new { subtitle = Subtitle, posY = posY, fontSize = fontSize, segmentId = Segment?.Id ?? -1 });
            }
            else
            {
                SubtitleOverlayPopup.IsOpen = false;
            }
        }

        /// <summary>
        /// 自由テキストオーバーレイを更新
        /// </summary>
        private void UpdateFreeTextOverlay()
        {
            if (FreeTextOverlayPopup == null || FreeTextOverlayContainer == null || VideoPlayer == null)
                return;

            if (FreeTextItems == null || FreeTextItems.Count == 0)
            {
                FreeTextOverlayContainer.ItemsSource = null;
                FreeTextOverlayPopup.IsOpen = false;
                return;
            }

            // VideoPlayerのサイズを基準に位置を計算
            var videoWidth = VideoPlayer.ActualWidth > 0 ? VideoPlayer.ActualWidth : 320;
            var videoHeight = VideoPlayer.ActualHeight > 0 ? VideoPlayer.ActualHeight : 180;

            // FreeTextItemをCanvasの座標に変換
            var displayItems = FreeTextItems.Select(item => new FreeTextDisplayItem
            {
                Text = item.Text,
                CanvasLeft = item.X * videoWidth,
                CanvasTop = item.Y * videoHeight
            }).ToList();

            FreeTextOverlayContainer.ItemsSource = displayItems;
            FreeTextOverlayContainer.Width = videoWidth;
            FreeTextOverlayContainer.Height = videoHeight;
            
            // Popupを動画の左上に配置（Canvasの座標は相対的）
            FreeTextOverlayPopup.HorizontalOffset = 0;
            FreeTextOverlayPopup.VerticalOffset = 0;
            FreeTextOverlayPopup.IsOpen = true;

            LogHelper.WriteLog(
                "VideoSegmentView.xaml.cs:UpdateFreeTextOverlay",
                "Free text overlay updated",
                new { itemCount = displayItems.Count, segmentId = Segment?.Id ?? -1 });
        }

        /// <summary>
        /// 自由テキスト表示用の内部クラス（Canvas座標を含む）
        /// </summary>
        private class FreeTextDisplayItem
        {
            public string Text { get; set; } = string.Empty;
            public double CanvasLeft { get; set; }
            public double CanvasTop { get; set; }
        }

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
                
                // 最大高さを制限（異常に大きくなるのを防止）
                // 最大600pxに制限（通常のフルHD動画には十分）
                const double MaxHeight = 600.0;
                adjustedHeight = Math.Min(adjustedHeight, MaxHeight);
                
                MainBorder.Height = adjustedHeight;
                
                // MainBorderの最大高さも設定（親要素からのはみ出しを防止）
                // ただし、最小高さ（150px）は保証する
                MainBorder.MinHeight = 150;
                MainBorder.MaxHeight = MaxHeight;
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
                // 古いSegmentの監視を解除
                if (e.OldValue is VideoSegment oldSegment)
                {
                    oldSegment.PropertyChanged -= view.OnSegmentPropertyChanged;
                }

                // 新しいSegmentの監視を開始
                if (e.NewValue is VideoSegment newSegment)
                {
                    newSegment.PropertyChanged += view.OnSegmentPropertyChanged;
                }

                view.UpdateView();
            }
        }

        /// <summary>
        /// Segmentのプロパティが変更されたときの処理
        /// </summary>
        private void OnSegmentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isDisposed)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                if (_isDisposed)
                    return;
                    
                switch (e.PropertyName)
                {
                    case nameof(VideoSegment.Title):
                        UpdateTitleOverlay();
                        break;
                    case nameof(VideoSegment.Subtitle):
                        UpdateSubtitleOverlay();
                        break;
                    case nameof(VideoSegment.FreeTextItems):
                        UpdateFreeTextOverlay();
                        break;
                    case nameof(VideoSegment.State):
                        UpdatePlaybackUI();
                        break;
                    case nameof(VideoSegment.Thumbnail):
                        // サムネイルが非同期で生成された場合にUIを更新
                        UpdateThumbnailDisplay();
                        break;
                }
            });
        }

        public VideoSegmentView()
        {
            InitializeComponent();
            Loaded += VideoSegmentView_Loaded;
            Unloaded += VideoSegmentView_Unloaded;
            SizeChanged += VideoSegmentView_SizeChanged;
            PreviewKeyDown += VideoSegmentView_PreviewKeyDown;
        }

        /// <summary>
        /// キー押下時の処理：再生中にEnterキーでCUT/HIDEコマンドを挿入
        /// 1回目のEnter: CUTコマンドを挿入（使わない部分の開始）
        /// 2回目のEnter: CUTコマンド + HIDEコマンドを挿入（使わない部分の終了）
        /// </summary>
        private void VideoSegmentView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            // 再生中でない場合は通常の動作
            if (!_isPlaying)
                return;

            // 現在の再生位置を取得（停止前に取得）
            // セグメントの開始時間を加算して絶対時間に変換
            var relativePositionSeconds = (_mediaPlayer?.Time ?? 0) / 1000.0;
            var absolutePositionSeconds = (Segment?.StartTime ?? 0) + relativePositionSeconds;
            
            LogHelper.WriteLog(
                "VideoSegmentView.xaml.cs:PreviewKeyDown",
                "Enter key pressed during playback",
                new { 
                    isPlaying = _isPlaying, 
                    relativePositionSeconds,
                    absolutePositionSeconds,
                    segmentStartTime = Segment?.StartTime ?? 0,
                    segmentId = _cachedSegmentId,
                    isHideModeEnabled = _timelineViewModel?.IsHideModeEnabled ?? false,
                    lastCutPosition = _timelineViewModel?.LastCutPosition
                });

            // CUTコマンドを挿入する前に再生を停止
            // （再生中にセグメントが再構築されるとスレッドアクセスエラーが発生するため）
            if (_timelineViewModel?.InsertCutCommandAction != null && absolutePositionSeconds > 0)
            {
                // まず再生を完全に停止してリソースを解放
                // StopAndCleanupSafeを使用してMediaPlayerを適切にDisposeする
                try
                {
                    // 現在再生中の参照をクリア
                    lock (_playbackLock)
                    {
                        if (_currentlyPlayingView == this)
                        {
                            _currentlyPlayingView = null;
                        }
                    }
                    
                    // セグメントの状態を先に更新
                    if (Segment != null)
                    {
                        Segment.State = SegmentState.Stopped;
                        if (_timelineViewModel.CurrentPlayingSegment.Value == Segment)
                        {
                            _timelineViewModel.CurrentPlayingSegment.Value = null;
                        }
                    }
                    
                    // MediaPlayerを完全にクリーンアップ（VideoViewからの解除とDisposeを含む）
                    // イベントハンドラを先に解除
                    _disposables?.Dispose();
                    _disposables = null;
                    
                    var playerToDispose = _mediaPlayer;
                    _mediaPlayer = null;
                    _isPlaying = false;
                    _pendingSeekTimeMs = null;
                    
                    if (playerToDispose != null)
                    {
                        // VideoViewからMediaPlayerを解除
                        if (VideoPlayer != null && !_isDisposed)
                        {
                            VideoPlayer.MediaPlayer = null;
                        }
                        
                        // MediaPlayerを停止（同期的に）
                        try
                        {
                            playerToDispose.Stop();
                        }
                        catch { }
                        
                        // Disposeはバックグラウンドで実行（UIスレッドをブロックしない）
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
                    
                    UpdatePlaybackUI();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:PreviewKeyDown",
                        "Error stopping playback",
                        new { error = ex.Message });
                }

                // HIDEモードの状態に応じて処理を分岐
                if (_timelineViewModel.IsHideModeEnabled && _timelineViewModel.LastCutPosition.HasValue)
                {
                    // 2回目のEnter: CUT + HIDE
                    var hideStartTime = _timelineViewModel.LastCutPosition.Value;
                    var hideEndTime = absolutePositionSeconds;

                    // CUTコマンドを挿入
                    _timelineViewModel.InsertCutCommandAction(absolutePositionSeconds);

                    // HIDEコマンドを挿入（前回のCUT位置から今回のCUT位置まで）
                    if (_timelineViewModel.InsertHideCommandAction != null && hideStartTime < hideEndTime)
                    {
                        _timelineViewModel.InsertHideCommandAction(hideStartTime, hideEndTime);
                    }

                    // HIDEモードをリセット
                    _timelineViewModel.IsHideModeEnabled = false;
                    _timelineViewModel.LastCutPosition = null;

                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:PreviewKeyDown",
                        "CUT + HIDE commands inserted (hide mode completed)",
                        new { cutPosition = absolutePositionSeconds, hideStartTime, hideEndTime });
                }
                else
                {
                    // 1回目のEnter: CUTのみ
                    _timelineViewModel.InsertCutCommandAction(absolutePositionSeconds);

                    // 次回のEnterでHIDEするために位置を記憶
                    _timelineViewModel.LastCutPosition = absolutePositionSeconds;
                    _timelineViewModel.IsHideModeEnabled = true;

                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:PreviewKeyDown",
                        "CUT command inserted (hide mode started)",
                        new { cutPosition = absolutePositionSeconds });
                }

                e.Handled = true;  // Enterキーの通常動作をキャンセル
            }
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
            
            // _disposablesを初期化（未初期化の場合）
            if (_disposables == null)
            {
                _disposables = new CompositeDisposable();
            }
            
            // ClipHeightが設定されていない場合、デフォルト値を設定
            if (ClipHeight <= 0)
            {
                ClipHeight = 220.0;
            }
            
            UpdateView();
            
            // TimelineViewModelのSelectedSegmentを監視して選択状態を表示
            SubscribeToSelectedSegment();
        }

        /// <summary>
        /// TimelineViewModelのSelectedSegmentを監視して選択状態を表示
        /// </summary>
        private void SubscribeToSelectedSegment()
        {
            // _disposablesが初期化されていることを確認
            if (_disposables == null)
            {
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:SubscribeToSelectedSegment",
                    "_disposables is null, cannot subscribe",
                    new { segmentId = Segment?.Id ?? -1 });
                return;
            }

            try
            {
                // 親を辿ってTimelinePageを見つけ、そこからTimelineViewModelを取得
                var parent = VisualTreeHelper.GetParent(this);
                while (parent != null)
                {
                    if (parent is FrameworkElement fe && fe.DataContext is TimelineViewModel timelineViewModel)
                    {
                        // TimelineViewModelへの参照を保存（再生位置の通知用）
                        _timelineViewModel = timelineViewModel;
                        
                        // SelectedSegmentを監視
                        timelineViewModel.SelectedSegment
                            .Subscribe(selectedSegment =>
                            {
                                // このセグメントが選択されているかチェック
                                var isSelected = selectedSegment != null && 
                                               Segment != null && 
                                               selectedSegment.Id == Segment.Id;
                                
                                // UIスレッドで選択状態を更新
                                Dispatcher.InvokeAsync(() => UpdateSelectionVisual(isSelected));
                            })
                            .AddTo(_disposables);
                        
                        LogHelper.WriteLog(
                            "VideoSegmentView.xaml.cs:SubscribeToSelectedSegment",
                            "Subscribed to SelectedSegment",
                            new { segmentId = Segment?.Id ?? -1 });
                        break;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:SubscribeToSelectedSegment",
                    "Error subscribing to SelectedSegment",
                    new { segmentId = Segment?.Id ?? -1, error = ex.Message });
            }
        }

        private void VideoSegmentView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 幅が変更された場合、高さを再計算
            if (e.WidthChanged && ClipHeight > 0)
            {
                UpdateClipHeight(ClipHeight);
            }
            
            // 再生中の場合のみ、サイズ変更時にテロップオーバーレイPopupの位置を再計算
            if (_isPlaying)
            {
                UpdateTitleOverlay();
                UpdateSubtitleOverlay();
                UpdateFreeTextOverlay();
            }
        }

        private void VideoSegmentView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Segmentの監視を解除
            if (Segment != null)
            {
                Segment.PropertyChanged -= OnSegmentPropertyChanged;
            }

            // Popupを閉じる
            if (TitleOverlayPopup != null)
                TitleOverlayPopup.IsOpen = false;
            if (SubtitleOverlayPopup != null)
                SubtitleOverlayPopup.IsOpen = false;
            if (FreeTextOverlayPopup != null)
                FreeTextOverlayPopup.IsOpen = false;

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
                // Fire-and-forget で例外を無視（Unobserved Task Exception を防ぐ）
                if (Segment.Thumbnail == null && !string.IsNullOrEmpty(Segment.VideoFilePath))
                {
                    _ = LoadThumbnailAsync().ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            // 例外を観察済みにする（ログは LoadThumbnailAsync 内で出力済み）
                            var _ = t.Exception;
                        }
                    }, TaskScheduler.Default);
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
            // Dispose済みまたはセグメントがない場合は何もしない
            if (_isDisposed || Segment == null || string.IsNullOrEmpty(Segment.VideoFilePath))
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

                // セグメントの開始位置のサムネイルを生成
                // ThumbnailProvider内でFreeze()が呼び出されるため、返されるBitmapSourceは既にスレッドセーフ
                // 分割されたセグメントの場合、StartTimeが0より大きいので、その位置のサムネイルを生成
                var thumbnailPosition = Segment.StartTime;
                
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:LoadThumbnailAsync",
                    "Generating thumbnail at segment start time",
                    new { segmentId = Segment.Id, thumbnailPosition = thumbnailPosition });
                
                var thumbnail = await thumbnailProvider.GenerateThumbnailAsync(
                    Segment.VideoFilePath,
                    timePosition: thumbnailPosition, // セグメントの開始位置
                    width: 320,
                    height: 180,
                    cancellationToken: default).ConfigureAwait(false);

                // 非同期処理後にDisposeされていたらUIにアクセスしない
                if (_isDisposed)
                    return;

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
                    
                    // UIスレッドでサムネイルを設定（Dispose済みの場合はスキップ）
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!_isDisposed && Segment != null)
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
                
                // 再生中はテロップオーバーレイPopupを表示
                UpdateTitleOverlay();
                UpdateSubtitleOverlay();
                UpdateFreeTextOverlay();
            }
            else
            {
                VideoPlayer.Visibility = Visibility.Collapsed;
                ThumbnailPlaceholder.Visibility = Visibility.Visible;
                CenterPlayButton.Opacity = 1.0;
                
                // サムネイル表示を更新
                UpdateThumbnailDisplay();
                
                // 停止中はテロップオーバーレイPopupを非表示
                if (TitleOverlayPopup != null)
                    TitleOverlayPopup.IsOpen = false;
                if (SubtitleOverlayPopup != null)
                    SubtitleOverlayPopup.IsOpen = false;
                if (FreeTextOverlayPopup != null)
                    FreeTextOverlayPopup.IsOpen = false;
            }
        }

        /// <summary>
        /// MediaPlayerを作成し、動画をロード
        /// </summary>
        private async Task<bool> InitializeAndLoadAsync()
        {
            if (Segment == null || string.IsNullOrEmpty(Segment.VideoFilePath))
                return false;

            // ネイティブスレッドからアクセスするためにセグメント情報をキャッシュ
            _cachedSegmentId = Segment.Id;
            _cachedSegmentEndTime = Segment.EndTime;

            try
            {
                // 共有LibVLCインスタンスを取得（公式ベストプラクティス: アプリ全体で1つのみ）
                var libVLC = Wideor_A23.App.SharedLibVLC;

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
                    
                    // MediaPlayerを停止（別スレッドで実行 - 公式ベストプラクティス）
                    await Task.Run(() =>
                    {
                        try
                        {
                            oldPlayer.Stop();
                        }
                        catch { }
                    });
                    
                    // 少し待機してから非同期で破棄（VideoViewの内部処理完了を待つ）
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        try
                        {
                            oldPlayer.Dispose();
                        }
                        catch { }
                    });
                }

                // 新しいMediaPlayerを作成
                _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(libVLC);
                SetupMediaPlayerEvents();

                // 動画をロード（元動画内の開始/終了位置をメディアオプションで指定）
                var media = new Media(libVLC, Segment.VideoFilePath, FromType.FromPath);
                
                // :start-time オプションで元動画内の開始位置を指定（秒単位）
                // MediaStartOffsetは元動画内の位置（タイムライン上の位置ではない）
                if (Segment.MediaStartOffset > 0)
                {
                    media.AddOption($":start-time={Segment.MediaStartOffset:F3}");
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:InitializeAndLoadAsync",
                        "Added start-time option",
                        new { segmentId = Segment.Id, mediaStartOffset = Segment.MediaStartOffset });
                }
                
                // :stop-time オプションで元動画内の終了位置を指定（秒単位）
                // MediaEndOffsetは元動画内の位置（タイムライン上の位置ではない）
                if (Segment.MediaEndOffset > 0 && Segment.MediaEndOffset < double.MaxValue)
                {
                    media.AddOption($":stop-time={Segment.MediaEndOffset:F3}");
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:InitializeAndLoadAsync",
                        "Added stop-time option",
                        new { segmentId = Segment.Id, mediaEndOffset = Segment.MediaEndOffset });
                }
                
                _mediaPlayer.Media = media;

                // VideoViewにMediaPlayerを設定する前に、VideoViewをVisibleに設定
                // これを先に行わないと、VLCが出力先を見つけられず別ウィンドウを作成してしまう
                var mediaPlayerSet = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (VideoPlayer != null && !_isDisposed)
                    {
                        // まずVideoViewを表示状態にする（VLCが正しい出力先を見つけるため）
                        VideoPlayer.Visibility = Visibility.Visible;
                        ThumbnailPlaceholder.Visibility = Visibility.Collapsed;
                        
                        // レイアウトを強制更新してVideoViewをロードさせる
                        VideoPlayer.UpdateLayout();
                        
                        // MediaPlayerを設定
                        VideoPlayer.MediaPlayer = _mediaPlayer;
                        mediaPlayerSet = true;
                    }
                }, DispatcherPriority.Loaded);
                
                // MediaPlayerが設定されなかった場合は少し待機して再試行
                if (!mediaPlayerSet && !_isDisposed)
                {
                    await Task.Delay(50);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (VideoPlayer != null && !_isDisposed && _mediaPlayer != null)
                        {
                            VideoPlayer.MediaPlayer = _mediaPlayer;
                        }
                    });
                }

                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:InitializeAndLoadAsync",
                    "MediaPlayer initialized",
                    new { segmentId = Segment.Id, videoPath = Segment.VideoFilePath, mediaPlayerSet });

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
            // 再生開始後にシークが必要な場合はシークを実行
            // 注意: このイベントはネイティブスレッドから呼び出されるため、
            // DependencyProperty (Segment) に直接アクセスしない
            if (_pendingSeekTimeMs.HasValue && _mediaPlayer != null)
            {
                var seekTime = _pendingSeekTimeMs.Value;
                _pendingSeekTimeMs = null;  // リセット
                
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:MediaPlayer_Playing",
                    "Seeking to pending position after play started",
                    new { seekTimeMs = seekTime, segmentId = _cachedSegmentId });
                
                // シークを実行（イベントハンドラ内でも安全）
                _mediaPlayer.Time = seekTime;
            }
            
            Dispatcher.InvokeAsync(() =>
            {
                _isPlaying = true;
                if (Segment != null) 
                {
                    Segment.State = SegmentState.Playing;
                    // TimelineViewModelに現在再生中のセグメントを通知
                    if (_timelineViewModel != null)
                    {
                        _timelineViewModel.CurrentPlayingSegment.Value = Segment;
                    }
                }
                UpdatePlaybackUI();
            });
        }

        private void MediaPlayer_Paused(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isPlaying = false;
                if (Segment != null) 
                {
                    Segment.State = SegmentState.Stopped;
                    // TimelineViewModelの現在再生中セグメントをクリア
                    if (_timelineViewModel != null && _timelineViewModel.CurrentPlayingSegment.Value == Segment)
                    {
                        _timelineViewModel.CurrentPlayingSegment.Value = null;
                    }
                }
                UpdatePlaybackUI();
            });
        }

        private void MediaPlayer_Stopped(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isPlaying = false;
                if (Segment != null) 
                {
                    Segment.State = SegmentState.Stopped;
                    // TimelineViewModelの現在再生中セグメントをクリア
                    if (_timelineViewModel != null && _timelineViewModel.CurrentPlayingSegment.Value == Segment)
                    {
                        _timelineViewModel.CurrentPlayingSegment.Value = null;
                    }
                }
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
                if (Segment != null) 
                {
                    Segment.State = SegmentState.Stopped;
                    // TimelineViewModelの現在再生中セグメントをクリア
                    if (_timelineViewModel != null && _timelineViewModel.CurrentPlayingSegment.Value == Segment)
                    {
                        _timelineViewModel.CurrentPlayingSegment.Value = null;
                    }
                }
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
                // 元動画の全体長を保持（シーク計算用）
                _totalDuration = e.Length / 1000.0;
                
                // 表示はセグメントのDurationを使用
                if (Segment != null)
                {
                    TotalTimeText.Text = FormatTime(Segment.Duration);
                    PlaceholderDurationText.Text = FormatTime(Segment.Duration);
                }
                else
                {
                    TotalTimeText.Text = FormatTime(_totalDuration);
                    PlaceholderDurationText.Text = FormatTime(_totalDuration);
                }
            });
        }

        private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (Segment == null) return;

                var currentTime = e.Time / 1000.0;
                
                // TimelineViewModelに現在の再生位置を通知
                if (_timelineViewModel != null)
                {
                    _timelineViewModel.CurrentSegmentPlaybackPosition.Value = currentTime;
                }
                
                // セグメントの終了位置を超えたら停止
                if (currentTime >= Segment.EndTime)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:MediaPlayer_TimeChanged",
                        "Reached segment end, stopping playback",
                        new { segmentId = Segment.Id, currentTime = currentTime, endTime = Segment.EndTime });
                    
                    // 終了処理を遅延実行（イベントハンドラ内での直接操作を避ける）
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(10);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            lock (_playbackLock)
                            {
                                if (_currentlyPlayingView == this)
                                {
                                    _currentlyPlayingView = null;
                                }
                            }
                            StopAndCleanupSafe();
                        });
                    });
                    return;
                }

                // セグメント内での相対位置を計算
                if (!_isDraggingSlider && Segment.Duration > 0)
                {
                    var segmentRelativeTime = currentTime - Segment.StartTime;
                    var progress = (segmentRelativeTime / Segment.Duration) * 100;
                    ProgressSlider.Value = Math.Min(100, Math.Max(0, progress));
                    CurrentTimeText.Text = FormatTime(segmentRelativeTime);
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
            _pendingSeekTimeMs = null;  // ペンディングシークをクリア

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
            _pendingSeekTimeMs = null;  // ペンディングシークをクリア

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

        /// <summary>
        /// 選択状態を視覚的に表示
        /// </summary>
        private void UpdateSelectionVisual(bool isSelected)
        {
            if (isSelected)
            {
                // アクセントカラーを使用
                var accentBrush = Application.Current.TryFindResource("AccentBrush") as SolidColorBrush;
                MainBorder.BorderBrush = accentBrush ?? new SolidColorBrush(Color.FromRgb(0, 122, 204));  // 青いハイライト（#007ACC）
                MainBorder.BorderThickness = new Thickness(3);
            }
            else
            {
                // 通常のボーダーカラーを使用
                var borderBrush = Application.Current.TryFindResource("BorderBrush") as SolidColorBrush;
                MainBorder.BorderBrush = borderBrush ?? new SolidColorBrush(Color.FromRgb(208, 208, 208));  // #D0D0D0（明るい灰色）
                MainBorder.BorderThickness = new Thickness(1);
            }
        }

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
                lock (_playbackLock)
                {
                    if (_currentlyPlayingView == this)
                    {
                        _currentlyPlayingView = null;
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
                        // 異なるスレッドの場合は非同期で実行（意図的にawaitしない）
                        _ = previousView.Dispatcher.BeginInvoke(() =>
                        {
                            previousView.StopAndCleanupSafe();
                        }, DispatcherPriority.Normal);
                    }
                }
                
                // 毎回新しいMediaPlayerとMediaを作成する
                // :start-time オプションは新しいMediaにしか適用されないため
                // 既存のMediaPlayerがある場合は先にクリーンアップ
                if (_mediaPlayer != null)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:OnPlayPauseClicked",
                        "Cleaning up existing MediaPlayer before reinitializing",
                        new { segmentId = Segment.Id });
                    
                    StopAndCleanup();
                }
                
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

                // 再生開始（:start-time オプションにより正しい位置から開始される）
                if (_mediaPlayer != null)
                {
                    LogHelper.WriteLog(
                        "VideoSegmentView.xaml.cs:OnPlayPauseClicked",
                        "Starting playback with media offset",
                        new { 
                            segmentId = Segment.Id, 
                            mediaStartOffset = Segment.MediaStartOffset, 
                            mediaEndOffset = Segment.MediaEndOffset,
                            timelineStart = Segment.StartTime,
                            timelineEnd = Segment.EndTime
                        });
                    
                    _mediaPlayer.Play();
                }
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
            
            if (_mediaPlayer != null && Segment != null && Segment.Duration > 0)
            {
                // スライダー位置からセグメント内の相対位置を計算し、元動画内の絶対位置に変換
                var relativePosition = (ProgressSlider.Value / 100.0) * Segment.Duration;
                var absolutePosition = Segment.StartTime + relativePosition;
                _mediaPlayer.Time = (long)(absolutePosition * 1000);
                
                LogHelper.WriteLog(
                    "VideoSegmentView.xaml.cs:OnProgressSliderMouseUp",
                    "Seeking within segment",
                    new { segmentId = Segment.Id, sliderValue = ProgressSlider.Value, relativePosition = relativePosition, absolutePosition = absolutePosition });
            }
        }

        private void OnProgressSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider && Segment != null && Segment.Duration > 0)
            {
                // スライダー位置からセグメント内の相対時間を計算
                var relativeTime = (e.NewValue / 100.0) * Segment.Duration;
                CurrentTimeText.Text = FormatTime(relativeTime);
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

            // Segmentの監視を解除
            if (Segment != null)
            {
                Segment.PropertyChanged -= OnSegmentPropertyChanged;
            }

            // Popupを閉じる
            if (TitleOverlayPopup != null)
                TitleOverlayPopup.IsOpen = false;
            if (SubtitleOverlayPopup != null)
                SubtitleOverlayPopup.IsOpen = false;
            if (FreeTextOverlayPopup != null)
                FreeTextOverlayPopup.IsOpen = false;

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
            
            // 注意: LibVLCは共有インスタンス（App.SharedLibVLC）なので、ここでは破棄しない
            // 公式ベストプラクティス: "Only create one LibVLC instance at all times"
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
