using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

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
            InitializeComponent();
            Loaded += FilmStripView_Loaded;
            Unloaded += FilmStripView_Unloaded;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FilmStripView view)
            {
                view.SubscribeToScrollCoordinator();
            }
        }

        private void FilmStripView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToScrollCoordinator();
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
        }
    }
}
