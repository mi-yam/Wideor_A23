using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// FilmStripView.xaml の相互作用ロジック
    /// 通常スクロール型：クリップサイズに応じて複数のクリップを表示
    /// </summary>
    public partial class FilmStripView : UserControl
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
    }
}
