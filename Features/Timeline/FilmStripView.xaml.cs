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
            if (FilmStripScrollViewer != null)
            {
                FilmStripScrollViewer.Loaded += (s, args) =>
                {
                    // EditorViewModelが設定されている場合のみ更新
                    if (ViewModel?.EditorViewModel != null)
                    {
                        UpdateVisibleThumbnailStates();
                    }
                };
            }
        }

        private void FilmStripView_Unloaded(object sender, RoutedEventArgs e)
        {
            _disposables?.Dispose();
            _disposables = null;
        }

        private void SubscribeToScrollCoordinator()
        {
            _disposables?.Dispose();
            _disposables = new CompositeDisposable();

            if (ViewModel == null)
                return;

            // ScrollCoordinatorにScrollViewerを登録
            var registration = ViewModel.ScrollCoordinator.RegisterScrollViewer(FilmStripScrollViewer);
            _disposables.Add(registration);

            // スクロール位置の変更を購読してScrollViewerを更新
            ViewModel.ScrollPosition
                .Subscribe(position =>
                {
                    if (FilmStripScrollViewer != null && FilmStripScrollViewer.ScrollableHeight > 0)
                    {
                        var offset = position * FilmStripScrollViewer.ScrollableHeight;
                        FilmStripScrollViewer.ScrollToVerticalOffset(offset);
                    }
                })
                .AddTo(_disposables);

            // スクロールイベントを監視して表示範囲を更新
            FilmStripScrollViewer.ScrollChanged += FilmStripScrollViewer_ScrollChanged;
            
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

        private void FilmStripScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            try
            {
                if (ViewModel == null || FilmStripScrollViewer == null)
                    return;

                UpdateVisibleThumbnailStates();
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

                if (ViewModel == null || FilmStripScrollViewer == null || ViewModel.EditorViewModel == null)
                    return;

                var viewportTop = FilmStripScrollViewer.VerticalOffset;
                var viewportBottom = viewportTop + FilmStripScrollViewer.ViewportHeight;
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
