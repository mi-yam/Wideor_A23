using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// FilmStripView.xaml の相互作用ロジック
    /// サムネイルリストを表示するビュー（Timeline機能専用）
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

        public FilmStripView()
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "FilmStripView.xaml.cs:Constructor",
                "FilmStripView constructor called",
                null);
            InitializeComponent();
            Loaded += FilmStripView_Loaded;
            Unloaded += FilmStripView_Unloaded;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FilmStripView view && e.NewValue is TimelineViewModel viewModel)
            {
                view.DataContext = viewModel; // DataContextをViewModelに設定
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "FilmStripView.xaml.cs:OnViewModelChanged",
                    "ViewModel changed",
                    new { hasViewModel = viewModel != null, thumbnailItemsCount = viewModel.ThumbnailItems.Count });
                view.SubscribeToScrollCoordinator();
            }
        }

        private void FilmStripView_Loaded(object sender, RoutedEventArgs e)
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "FilmStripView.xaml.cs:Loaded",
                "FilmStripView loaded",
                new { hasViewModel = ViewModel != null, hasDataContext = DataContext != null, thumbnailItemsCount = ViewModel?.ThumbnailItems.Count ?? 0 });
            SubscribeToScrollCoordinator();
            
            // 初期表示範囲を更新（EditorViewModelが設定されている場合のみ）
            var scrollViewer = GetScrollViewer(FilmStripListBox);
            if (scrollViewer != null)
            {
                scrollViewer.Loaded += (s, args) =>
                {
                    // EditorViewModelが設定されている場合のみ更新
                    if (ViewModel?.EditorViewModel != null)
                    {
                        UpdateVisibleThumbnailStates();
                    }
                };
            }
        }

        /// <summary>
        /// ListBox内のScrollViewerを取得
        /// </summary>
        private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj == null) return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
                var childOfChild = GetScrollViewer(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        private void FilmStripView_Unloaded(object sender, RoutedEventArgs e)
        {
            _disposables?.Dispose();
            _disposables = null;
            
            // タイマーをクリーンアップ
            _scrollThrottleTimer?.Stop();
            _scrollThrottleTimer = null;
        }

        private void SubscribeToScrollCoordinator()
        {
            _disposables?.Dispose();
            _disposables = new CompositeDisposable();

            if (ViewModel == null)
                return;

            // ListBox内のScrollViewerを取得
            var scrollViewer = GetScrollViewer(FilmStripListBox);
            if (scrollViewer == null)
                return;

            // ScrollCoordinatorにScrollViewerを登録
            var registration = ViewModel.ScrollCoordinator.RegisterScrollViewer(scrollViewer);
            _disposables.Add(registration);

            // スクロール位置の変更を購読してScrollViewerを更新（スロットル付き、UIスレッドで実行）
            ViewModel.ScrollPosition
                .Throttle(TimeSpan.FromMilliseconds(16)) // 60fps相当の更新レート
                .Subscribe(position =>
                {
                    // UIスレッドで実行されることを保証
                    if (Dispatcher.CheckAccess())
                    {
                        if (scrollViewer != null && scrollViewer.ScrollableHeight > 0)
                        {
                            var offset = position * scrollViewer.ScrollableHeight;
                            scrollViewer.ScrollToVerticalOffset(offset);
                        }
                    }
                    else
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (scrollViewer != null && scrollViewer.ScrollableHeight > 0)
                            {
                                var offset = position * scrollViewer.ScrollableHeight;
                                scrollViewer.ScrollToVerticalOffset(offset);
                            }
                        }, DispatcherPriority.Normal);
                    }
                })
                .AddTo(_disposables);

            // スクロールイベントを監視して表示範囲を更新（スロットル付き）
            scrollViewer.ScrollChanged += FilmStripScrollViewer_ScrollChanged;
            
            // EditorViewModelのSceneBlocksが変更されたら表示範囲を更新
            SubscribeToEditorViewModel();
        }

        private void SubscribeToEditorViewModel()
        {
            if (ViewModel?.EditorViewModel == null)
                return;

            // EditorViewModelのSceneBlocksが変更されたら表示範囲を更新
            ViewModel.EditorViewModel.SceneBlocks
                .Throttle(TimeSpan.FromMilliseconds(100)) // 100msのスロットル
                .Subscribe(_ =>
                {
                    // UIスレッドで実行
                    if (Dispatcher.CheckAccess())
                    {
                        UpdateVisibleThumbnailStates();
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(new Action(UpdateVisibleThumbnailStates), DispatcherPriority.Normal);
                    }
                })
                .AddTo(_disposables);
        }

        private System.Windows.Threading.DispatcherTimer? _scrollThrottleTimer;

        private void FilmStripScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            try
            {
                if (ViewModel == null)
                    return;

                // スクロールイベントをスロットル（100ms間隔で更新）
                if (_scrollThrottleTimer == null)
                {
                    _scrollThrottleTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    _scrollThrottleTimer.Tick += (s, args) =>
                    {
                        _scrollThrottleTimer.Stop();
                        UpdateVisibleThumbnailStates();
                    };
                }

                // タイマーをリセット
                _scrollThrottleTimer.Stop();
                _scrollThrottleTimer.Start();
            }
            catch (Exception ex)
            {
                // エラーをログに記録（クラッシュを防ぐ）
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "FilmStripView.xaml.cs:FilmStripScrollViewer_ScrollChanged",
                    "Error in scroll changed handler",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        private void UpdateVisibleThumbnailStates()
        {
            try
            {
                // UIスレッドで実行されていることを確認
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.InvokeAsync(UpdateVisibleThumbnailStates);
                    return;
                }

                if (ViewModel == null || ViewModel.EditorViewModel == null)
                    return;

                var scrollViewer = GetScrollViewer(FilmStripListBox);
                if (scrollViewer == null)
                    return;

                var viewportTop = scrollViewer.VerticalOffset;
                var viewportBottom = viewportTop + scrollViewer.ViewportHeight;
                var sceneBlocks = ViewModel.EditorViewModel.SceneBlocks.Value ?? Array.Empty<SceneBlock>();

                ViewModel.UpdateVisibleThumbnailStates(viewportTop, viewportBottom, sceneBlocks);
            }
            catch (Exception ex)
            {
                // エラーをログに記録（クラッシュを防ぐ）
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "FilmStripView.xaml.cs:UpdateVisibleThumbnailStates",
                    "Error updating visible thumbnail states",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
            }
        }
    }
}
