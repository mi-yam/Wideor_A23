using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System.Windows.Threading; // ← 追加

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// TimeRulerView.xaml の相互作用ロジック
    /// 時間目盛りを描画するビュー（Timeline機能専用）
    /// </summary>
    public partial class TimeRulerView : UserControl
    {
        /// <summary>
        /// ViewModelプロパティ
        /// </summary>
        public TimelineViewModel ViewModel
        {
            get => (TimelineViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(TimelineViewModel),
                typeof(TimeRulerView),
                new PropertyMetadata(null, OnViewModelChanged));

        private IDisposable? _subscription;
        private System.Reactive.Disposables.CompositeDisposable? _disposables;
        private double? _pendingScrollPosition = null;
        private bool _isUpdatingRuler = false;
        private bool _isLayoutUpdatedSubscribed = false;

        public TimeRulerView()
        {
            InitializeComponent();
            Loaded += TimeRulerView_Loaded;
            Unloaded += TimeRulerView_Unloaded;
            LayoutUpdated += TimeRulerView_LayoutUpdated;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimeRulerView view)
            {
                view.SubscribeToViewModel();
                view.SubscribeToScrollCoordinator();
            }
        }

        private void TimeRulerView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToViewModel();
            SubscribeToScrollCoordinator();
            UpdateRuler();
        }

        private void TimeRulerView_Unloaded(object sender, RoutedEventArgs e)
        {
            _subscription?.Dispose();
            _subscription = null;
            _disposables?.Dispose();
            _disposables = null;
            LayoutUpdated -= TimeRulerView_LayoutUpdated;
        }

        private void TimeRulerView_LayoutUpdated(object? sender, EventArgs e)
        {
            // レイアウト完了後、保留中のスクロール位置を復元
            if (_pendingScrollPosition.HasValue && TimeRulerScrollViewer != null)
            {
                try
                {
                    if (TimeRulerScrollViewer.ScrollableHeight > 0)
                    {
                        var offset = _pendingScrollPosition.Value * TimeRulerScrollViewer.ScrollableHeight;
                        TimeRulerScrollViewer.ScrollToVerticalOffset(offset);
                        
                        // #region agent log
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimeRulerView.xaml.cs:LayoutUpdated",
                            "Scroll position restored",
                            new { offset, scrollableHeight = TimeRulerScrollViewer.ScrollableHeight, pendingScrollPosition = _pendingScrollPosition.Value });
                        // #endregion
                        
                        var pendingValue = _pendingScrollPosition.Value;
                        _pendingScrollPosition = null;
                        LayoutUpdated -= TimeRulerView_LayoutUpdated; // 一度だけ実行
                        _isLayoutUpdatedSubscribed = false;
                        
                        // イベントハンドラーを削除した後、再度追加しないようにする
                        return;
                    }
                    else
                    {
                        // ScrollableHeightが0の場合は、次のレイアウト更新を待つ
                        // #region agent log
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimeRulerView.xaml.cs:LayoutUpdated",
                            "ScrollableHeight is 0, waiting for next layout update",
                            new { pendingScrollPosition = _pendingScrollPosition.Value });
                        // #endregion
                    }
                }
                catch (Exception ex)
                {
                    // #region agent log
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimeRulerView.xaml.cs:LayoutUpdated",
                        "Failed to restore scroll position",
                        new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                    // #endregion
                    _pendingScrollPosition = null;
                    LayoutUpdated -= TimeRulerView_LayoutUpdated;
                    _isLayoutUpdatedSubscribed = false;
                }
            }
        }

        private void SubscribeToViewModel()
        {
            _subscription?.Dispose();

            if (ViewModel == null)
                return;

            // PixelsPerSecondまたはTotalDurationが変更されたら目盛りを更新
            // Throttleを使用して更新頻度を抑制（ズーム操作中の頻繁な更新を防ぐ）
            _subscription = ViewModel.PixelsPerSecond
                .AsObservable()
                .CombineLatest(ViewModel.TotalDuration.AsObservable(), (pps, duration) => new { PPS = pps, Duration = duration })
                .Throttle(TimeSpan.FromMilliseconds(100)) // 100ms間隔で更新
                .Subscribe(_ =>
                {
                    // UIスレッドで実行
                    if (Dispatcher.CheckAccess())
                    {
                        UpdateRuler();
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(new Action(UpdateRuler), DispatcherPriority.Normal);
                    }
                });
        }

        private void SubscribeToScrollCoordinator()
        {
            _disposables?.Dispose();
            _disposables = new System.Reactive.Disposables.CompositeDisposable();

            if (ViewModel == null)
                return;

            // ScrollCoordinatorにScrollViewerを登録
            var registration = ViewModel.ScrollCoordinator.RegisterScrollViewer(TimeRulerScrollViewer);
            _disposables.Add(registration);

            // スクロール位置の変更を購読してScrollViewerを更新
            ViewModel.ScrollPosition
                .Subscribe(position =>
                {
                    if (TimeRulerScrollViewer != null && TimeRulerScrollViewer.ScrollableHeight > 0)
                    {
                        var offset = position * TimeRulerScrollViewer.ScrollableHeight;
                        TimeRulerScrollViewer.ScrollToVerticalOffset(offset);
                    }
                })
                .AddTo(_disposables);
        }

        private void UpdateRuler()
        {
            try
            {
                if (ViewModel == null || _isUpdatingRuler)
                    return;

                _isUpdatingRuler = true;

                // #region agent log
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimeRulerView.xaml.cs:UpdateRuler",
                    "UpdateRuler called",
                    new { hasViewModel = ViewModel != null, pixelsPerSecond = ViewModel?.PixelsPerSecond.Value ?? 0, totalDuration = ViewModel?.TotalDuration.Value ?? 0 });
                // #endregion

                RulerCanvas.Children.Clear();

                // 固定スケール: 1秒 = 100ピクセル
                var pixelsPerSecond = 100.0;
                var totalDuration = ViewModel.TotalDuration.Value;
                var canvasWidth = ActualWidth > 0 ? ActualWidth : 60;
                var canvasHeight = ActualHeight > 0 ? ActualHeight : 600;

                if (totalDuration <= 0)
                {
                    // 初期化時やTotalDurationが未設定の場合は正常な動作なので、ログレベルを下げる
                    // #region agent log
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimeRulerView.xaml.cs:UpdateRuler",
                        "TotalDuration not set yet, skipping update",
                        new { totalDuration, hasViewModel = ViewModel != null });
                    // #endregion
                    return;
                }

                var totalHeight = totalDuration * pixelsPerSecond;

                // 現在のスクロール位置を保存（Canvasの高さ変更後に復元するため）
                var currentScrollPosition = 0.0;
                if (TimeRulerScrollViewer != null && TimeRulerScrollViewer.ScrollableHeight > 0)
                {
                    currentScrollPosition = TimeRulerScrollViewer.VerticalOffset / TimeRulerScrollViewer.ScrollableHeight;
                }

                // #region agent log
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimeRulerView.xaml.cs:UpdateRuler",
                    "Setting canvas size",
                    new { totalHeight, canvasWidth, scrollableHeight = TimeRulerScrollViewer?.ScrollableHeight ?? 0, currentScrollPosition });
                // #endregion

                // Canvasの高さを設定（スクロール可能にするため）
                RulerCanvas.Height = totalHeight;
                RulerCanvas.Width = canvasWidth;

            // 0秒から9秒まで、1秒間隔で描画
            var startTime = 0.0;
            var endTime = Math.Min(9.0, totalDuration);
            var interval = 1.0; // 1秒間隔

            // 目盛りを描画
            for (double time = startTime; time <= endTime; time += interval)
            {
                try
                {
                    // 固定スケール: 1秒 = 100ピクセル
                    var y = time * pixelsPerSecond;

                    // メイン目盛り（縦線）
                    var line = new Line
                    {
                        X1 = canvasWidth - 20, // 右側に線を描画
                        Y1 = y,
                        X2 = canvasWidth,
                        Y2 = y,
                        Stroke = new SolidColorBrush(Colors.White),
                        StrokeThickness = 1.0
                    };
                    RulerCanvas.Children.Add(line);

                    // 数字ラベル（0.から9.まで）
                    var primaryTextBrush = Application.Current.TryFindResource("PrimaryTextBrush") as SolidColorBrush;
                    var textBlock = new TextBlock
                    {
                        Text = $"{(int)time}.",
                        Foreground = primaryTextBrush ?? new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), // プライマリテキスト色
                        FontSize = 12,
                        FontWeight = FontWeights.Normal,
                        LayoutTransform = new RotateTransform(0), // 縦方向なので回転不要
                        Margin = new Thickness(2, y - 6, 0, 0)
                    };
                    RulerCanvas.Children.Add(textBlock);

                    // 0.1秒間隔の細かい目盛り（数字の間）
                    if (time < endTime)
                    {
                        for (int i = 1; i < 10; i++)
                        {
                            var subTime = time + (i * 0.1);
                            if (subTime > endTime) break;
                            
                            var subY = subTime * pixelsPerSecond;
                            var isLongTick = (i == 5); // 5つ目を少し長く
                            
                            var subLine = new Line
                            {
                                X1 = canvasWidth - (isLongTick ? 10 : 5),
                                Y1 = subY,
                                X2 = canvasWidth,
                                Y2 = subY,
                                Stroke = new SolidColorBrush(Colors.Gray),
                                StrokeThickness = 0.5,
                                Opacity = 0.7
                            };
                            RulerCanvas.Children.Add(subLine);
                        }
                    }
                }
                catch (Exception tickEx)
                {
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimeRulerView.xaml.cs:UpdateRuler",
                            "Exception adding tick",
                            new { time, interval, exceptionType = tickEx.GetType().Name, message = tickEx.Message });
                    }
                    catch { }
                    // #endregion
                    // 個別の目盛りのエラーは無視して続行
                }
            }

                // スクロール位置を復元（Canvasの高さ変更後、レイアウトが完了してから）
                if (TimeRulerScrollViewer != null && currentScrollPosition > 0)
                {
                    // 保留中のスクロール位置を設定（LayoutUpdatedイベントで復元される）
                    _pendingScrollPosition = currentScrollPosition;
                    
                    // LayoutUpdatedイベントを一度だけ購読
                    if (!_isLayoutUpdatedSubscribed)
                    {
                        LayoutUpdated += TimeRulerView_LayoutUpdated;
                        _isLayoutUpdatedSubscribed = true;
                    }
                    
                    // #region agent log
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimeRulerView.xaml.cs:UpdateRuler",
                        "Pending scroll position set",
                        new { pendingScrollPosition = _pendingScrollPosition, currentScrollPosition, isLayoutUpdatedSubscribed = _isLayoutUpdatedSubscribed });
                    // #endregion
                }

                // #region agent log
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimeRulerView.xaml.cs:UpdateRuler",
                    "UpdateRuler completed",
                    new { childrenCount = RulerCanvas.Children.Count, canvasHeight = RulerCanvas.Height, canvasWidth = RulerCanvas.Width });
                // #endregion
            }
            catch (Exception ex)
            {
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimeRulerView.xaml.cs:UpdateRuler",
                        "UpdateRuler failed with exception",
                        new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, innerException = ex.InnerException?.ToString() });
                }
                catch { }
                // #endregion
                // 例外を再スローしない（アプリケーションをクラッシュさせないため）
                // throw;
            }
            finally
            {
                _isUpdatingRuler = false;
            }
        }

        /// <summary>
        /// ピクセル/秒に基づいて適切な時間間隔を計算
        /// </summary>
        private double CalculateInterval(double pixelsPerSecond)
        {
            // 目盛り間隔が50ピクセル以上になるように調整
            var targetPixelInterval = 50.0;
            var timeInterval = targetPixelInterval / pixelsPerSecond;

            // 適切な間隔に丸める（1秒、5秒、10秒、30秒、1分、5分など）
            if (timeInterval <= 1)
                return 1;
            else if (timeInterval <= 5)
                return 5;
            else if (timeInterval <= 10)
                return 10;
            else if (timeInterval <= 30)
                return 30;
            else if (timeInterval <= 60)
                return 60;
            else if (timeInterval <= 300)
                return 300;
            else
                return 600; // 10分
        }

        /// <summary>
        /// 目盛りのレベル
        /// </summary>
        private enum TickLevel
        {
            Minor,   // 小目盛り
            Medium,  // 中目盛り
            Major    // 大目盛り
        }

        /// <summary>
        /// 目盛りのレベルを判定
        /// </summary>
        private TickLevel GetTickLevel(double time, double interval)
        {
            // メジャー: 1分、5分、10分などの大きな区切り
            if (Math.Abs(time % 60) < 0.01 || Math.Abs(time % 300) < 0.01)
                return TickLevel.Major;
            
            // ミディアム: 10秒、30秒などの区切り
            if (Math.Abs(time % 10) < 0.01 || Math.Abs(time % 30) < 0.01)
                return TickLevel.Medium;
            
            // マイナー: それ以外
            return TickLevel.Minor;
        }

        /// <summary>
        /// 目盛りのブラシを取得
        /// </summary>
        private Brush GetTickBrush(TickLevel level)
        {
            // アプリケーションリソースから色を取得
            var primaryTextBrush = Application.Current.TryFindResource("PrimaryTextBrush") as SolidColorBrush;
            var tertiaryTextBrush = Application.Current.TryFindResource("TertiaryTextBrush") as SolidColorBrush;
            var borderBrush = Application.Current.TryFindResource("BorderBrush") as SolidColorBrush;
            
            return level switch
            {
                TickLevel.Major => primaryTextBrush ?? new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), // プライマリテキスト色（太い線）
                TickLevel.Medium => tertiaryTextBrush ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), // ターシャリテキスト色（中間線）
                TickLevel.Minor => borderBrush ?? new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)), // ボーダー色（細い線）
                _ => borderBrush ?? new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0))
            };
        }

        /// <summary>
        /// 目盛りの太さを取得
        /// </summary>
        private double GetTickThickness(TickLevel level)
        {
            return level switch
            {
                TickLevel.Major => 2.0,
                TickLevel.Medium => 1.0,
                TickLevel.Minor => 0.5,
                _ => 0.5
            };
        }

        /// <summary>
        /// 目盛りの不透明度を取得
        /// </summary>
        private double GetTickOpacity(TickLevel level)
        {
            return level switch
            {
                TickLevel.Major => 1.0,
                TickLevel.Medium => 0.8,
                TickLevel.Minor => 0.5,
                _ => 0.5
            };
        }

        /// <summary>
        /// 時間を文字列にフォーマット
        /// </summary>
        private string FormatTime(double seconds)
        {
            var timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            else
            {
                return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            try
            {
                UpdateRuler();
            }
            catch (Exception ex)
            {
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimeRulerView.xaml.cs:OnRenderSizeChanged",
                        "Exception in UpdateRuler",
                        new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                }
                catch { }
                // #endregion
                // 例外を無視（アプリケーションをクラッシュさせないため）
            }
        }
    }
}
