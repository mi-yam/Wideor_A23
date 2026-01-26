using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// FilmStripView.xaml の相互作用ロジック
    /// 通常スクロール型：クリップサイズに応じて複数のクリップを表示
    /// スナップスクロール対応：PowerPointライクにセグメント単位でスナップ
    /// </summary>
    public partial class FilmStripView : UserControl
    {
        /// <summary>
        /// スナップスクロールを有効にするかどうか
        /// </summary>
        public bool EnableSnapScroll { get; set; } = true;

        // スナップアニメーション中かどうか
        private bool _isSnapping = false;
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
                typeof(FilmStripView),
                new PropertyMetadata(null, OnViewModelChanged));

        private CompositeDisposable? _disposables;
        private int _totalClips = 0;
        private double _clipHeight = 220.0; // デフォルトのクリップ高さ

        public FilmStripView()
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "FilmStripView.xaml.cs:Constructor",
                "FilmStripView constructor called",
                null);
            InitializeComponent();
            Loaded += FilmStripView_Loaded;
            Unloaded += FilmStripView_Unloaded;
            SizeChanged += FilmStripView_SizeChanged;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FilmStripView view && e.NewValue is TimelineViewModel viewModel)
            {
                view.DataContext = viewModel;
                view.SubscribeToVideoSegments();
            }
        }

        private void FilmStripView_Loaded(object sender, RoutedEventArgs e)
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "FilmStripView.xaml.cs:Loaded",
                "FilmStripView loaded",
                new { hasViewModel = ViewModel != null, actualHeight = ActualHeight });
            
            UpdateClipHeight();
            SubscribeToVideoSegments();
            UpdateClipIndicator();
        }

        private void FilmStripView_Unloaded(object sender, RoutedEventArgs e)
        {
            _disposables?.Dispose();
            _disposables = null;
        }

        private void FilmStripView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 幅または高さが変更された場合、クリップの高さを更新
            if (e.WidthChanged || e.HeightChanged)
            {
                UpdateClipHeight();
            }
        }

        /// <summary>
        /// クリップの高さを更新（FilmStripViewの幅に基づいて計算）
        /// 動画のアスペクト比を考慮して、動画がはみ出ないように高さを設定
        /// </summary>
        private void UpdateClipHeight()
        {
            // 利用可能な幅を取得
            var availableWidth = ActualWidth > 0 ? ActualWidth : 640;
            
            // 16:9のアスペクト比を仮定して、デフォルトの動画高さを計算
            // 動画高さ + ヘッダー(25px) + コントロールバー(35px) = 総高さ
            var defaultVideoHeight = availableWidth * (9.0 / 16.0);
            var newHeight = Math.Max(150, defaultVideoHeight + 60);
            
            if (Math.Abs(_clipHeight - newHeight) > 1)
            {
                _clipHeight = newHeight;
                UpdateAllClipsHeight();
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "FilmStripView.xaml.cs:UpdateClipHeight",
                    "Clip height updated",
                    new { clipHeight = _clipHeight, actualWidth = ActualWidth, actualHeight = ActualHeight });
            }
        }

        /// <summary>
        /// 全てのVideoSegmentViewのClipHeightを更新
        /// </summary>
        private void UpdateAllClipsHeight()
        {
            if (FilmStripItemsControl == null) return;
            
            foreach (var item in FilmStripItemsControl.Items)
            {
                var container = FilmStripItemsControl.ItemContainerGenerator.ContainerFromItem(item);
                if (container != null)
                {
                    var videoSegmentView = FindVisualChild<VideoSegmentView>(container);
                    if (videoSegmentView != null)
                    {
                        videoSegmentView.ClipHeight = _clipHeight;
                    }
                }
            }
        }

        /// <summary>
        /// ビジュアルツリーから子要素を検索
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }
                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void SubscribeToVideoSegments()
        {
            _disposables?.Dispose();
            _disposables = new CompositeDisposable();

            var viewModel = ViewModel;
            if (viewModel == null) return;

            // ItemsSourceを設定
            FilmStripItemsControl.ItemsSource = viewModel.VideoSegments;

            // コレクション変更を監視
            if (viewModel.VideoSegments is INotifyCollectionChanged notifyCollection)
            {
                notifyCollection.CollectionChanged += OnVideoSegmentsChanged;
                _disposables.Add(Disposable.Create(() =>
                {
                    notifyCollection.CollectionChanged -= OnVideoSegmentsChanged;
                }));
            }

            // 初期状態を更新
            _totalClips = viewModel.VideoSegments.Count;
            UpdateClipIndicator();
        }

        private void OnVideoSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _totalClips = ViewModel?.VideoSegments.Count ?? 0;
                
                // 新しいクリップが追加された場合、最後までスクロール
                if (e.Action == NotifyCollectionChangedAction.Add && _totalClips > 0)
                {
                    // レイアウト更新後にスクロールとクリップサイズ更新
                    Dispatcher.InvokeAsync(() =>
                    {
                        UpdateAllClipsHeight();
                        FilmStripScrollViewer.ScrollToEnd();
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else
                {
                    // その他の変更時もクリップサイズを更新
                    UpdateAllClipsHeight();
                }
                
                UpdateClipIndicator();
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "FilmStripView.xaml.cs:OnVideoSegmentsChanged",
                    "Collection changed",
                    new { action = e.Action.ToString(), totalClips = _totalClips });
            });
        }

        /// <summary>
        /// スクロール変更時にインジケーターを更新
        /// </summary>
        private void FilmStripScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateClipIndicator();
        }

        /// <summary>
        /// クリップインジケーターを更新（表示中のクリップ範囲を表示）
        /// </summary>
        private void UpdateClipIndicator()
        {
            if (_totalClips == 0)
            {
                ClipIndexIndicator.Text = "0 / 0";
                return;
            }

            // 現在表示中のクリップを検出
            var visibleClips = GetVisibleClipIndices();
            
            if (visibleClips.Count == 0)
            {
                ClipIndexIndicator.Text = $"1-{_totalClips} / {_totalClips}";
            }
            else if (visibleClips.Count == 1)
            {
                ClipIndexIndicator.Text = $"{visibleClips[0] + 1} / {_totalClips}";
            }
            else
            {
                var first = visibleClips[0] + 1;
                var last = visibleClips[visibleClips.Count - 1] + 1;
                ClipIndexIndicator.Text = $"{first}-{last} / {_totalClips}";
            }
        }

        /// <summary>
        /// 現在表示中のクリップのインデックスを取得
        /// </summary>
        private List<int> GetVisibleClipIndices()
        {
            var visibleIndices = new List<int>();
            
            if (FilmStripItemsControl == null || _totalClips == 0) 
                return visibleIndices;

            var scrollViewerTop = 0.0;
            var scrollViewerBottom = FilmStripScrollViewer.ActualHeight;
            var verticalOffset = FilmStripScrollViewer.VerticalOffset;

            for (int i = 0; i < _totalClips; i++)
            {
                var container = FilmStripItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                // コンテナの位置を取得
                var transform = container.TransformToAncestor(FilmStripScrollViewer);
                var topLeft = transform.Transform(new Point(0, 0));
                var containerTop = topLeft.Y;
                var containerBottom = containerTop + container.ActualHeight;

                // 表示領域と重なっているかチェック
                if (containerBottom > scrollViewerTop && containerTop < scrollViewerBottom)
                {
                    visibleIndices.Add(i);
                }
            }

            return visibleIndices;
        }

        /// <summary>
        /// 指定したインデックスのクリップにスクロール
        /// </summary>
        public void ScrollToClipIndex(int index)
        {
            if (_totalClips == 0 || index < 0 || index >= _totalClips) return;

            var container = FilmStripItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (container != null)
            {
                container.BringIntoView();
            }
        }

        /// <summary>
        /// 現在表示中の最初のクリップインデックス
        /// </summary>
        public int CurrentClipIndex
        {
            get
            {
                var visible = GetVisibleClipIndices();
                return visible.Count > 0 ? visible[0] : 0;
            }
        }

        /// <summary>
        /// マウスホイールイベント - スナップスクロール対応
        /// Ctrlキーが押されていない場合はセグメント単位でスナップ
        /// </summary>
        public void FilmStripScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // スナップスクロールが無効の場合は通常スクロール
            if (!EnableSnapScroll)
                return;

            // Ctrlキーが押されている場合は通常スクロール（ズーム用に予約）
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                return;

            // スナップアニメーション中は無視
            if (_isSnapping)
            {
                e.Handled = true;
                return;
            }

            // クリップがない場合は通常スクロール
            if (_totalClips == 0)
                return;

            e.Handled = true;

            // 現在表示中のクリップインデックス
            var currentIndex = CurrentClipIndex;

            // スクロール方向に応じて次のクリップへ移動
            int nextIndex;
            if (e.Delta > 0)
            {
                // 上方向スクロール: 前のクリップへ
                nextIndex = Math.Max(0, currentIndex - 1);
            }
            else
            {
                // 下方向スクロール: 次のクリップへ
                nextIndex = Math.Min(_totalClips - 1, currentIndex + 1);
            }

            // 同じクリップなら何もしない
            if (nextIndex == currentIndex)
                return;

            // スナップアニメーション付きでスクロール
            ScrollToClipIndexWithAnimation(nextIndex);
        }

        /// <summary>
        /// スナップアニメーション付きで指定インデックスのクリップにスクロール
        /// </summary>
        private void ScrollToClipIndexWithAnimation(int index)
        {
            if (_totalClips == 0 || index < 0 || index >= _totalClips)
                return;

            var container = FilmStripItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (container == null)
            {
                // コンテナがまだ生成されていない場合は直接スクロール
                ScrollToClipIndex(index);
                return;
            }

            try
            {
                // コンテナの位置を取得
                var transform = container.TransformToAncestor(FilmStripScrollViewer);
                var targetPosition = transform.Transform(new Point(0, 0)).Y;

                // 目標スクロール位置
                var currentOffset = FilmStripScrollViewer.VerticalOffset;
                var targetOffset = currentOffset + targetPosition;

                // スクロール範囲を制限
                targetOffset = Math.Max(0, Math.Min(targetOffset, FilmStripScrollViewer.ScrollableHeight));

                // アニメーションを開始
                _isSnapping = true;

                // DoubleAnimationを使用してスムーズスクロール
                var animation = new DoubleAnimation
                {
                    From = currentOffset,
                    To = targetOffset,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                animation.Completed += (s, e) =>
                {
                    _isSnapping = false;
                    UpdateClipIndicator();
                };

                // アニメーションを適用（ScrollViewerのVerticalOffsetは直接アニメーションできないため、タイマーベースで実装）
                AnimateScrollViewer(currentOffset, targetOffset, 250);
            }
            catch (Exception ex)
            {
                _isSnapping = false;
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "FilmStripView:ScrollToClipIndexWithAnimation",
                    "Animation failed, falling back to direct scroll",
                    new { index = index, error = ex.Message });

                // フォールバック: 直接スクロール
                ScrollToClipIndex(index);
            }
        }

        /// <summary>
        /// ScrollViewerをアニメーションでスクロール
        /// </summary>
        private void AnimateScrollViewer(double from, double to, int durationMs)
        {
            var startTime = DateTime.Now;
            var duration = TimeSpan.FromMilliseconds(durationMs);

            System.Windows.Threading.DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromMilliseconds(16) // 約60fps
            };

            timer.Tick += (s, e) =>
            {
                var elapsed = DateTime.Now - startTime;
                var progress = Math.Min(1.0, elapsed.TotalMilliseconds / durationMs);

                // イージング関数（CubicEaseOut）を適用
                var easedProgress = 1.0 - Math.Pow(1.0 - progress, 3);

                var currentOffset = from + (to - from) * easedProgress;
                FilmStripScrollViewer.ScrollToVerticalOffset(currentOffset);

                if (progress >= 1.0)
                {
                    timer.Stop();
                    _isSnapping = false;
                    UpdateClipIndicator();
                }
            };

            timer.Start();
        }
    }
}
