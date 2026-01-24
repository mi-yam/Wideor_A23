using System.Windows;
using System.Windows.Controls;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// TimelinePage.xaml の相互作用ロジック
    /// TimeRulerViewとFilmStripViewを左右に配置する親ビュー（Timeline機能専用）
    /// </summary>
    public partial class TimelinePage : UserControl
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
                typeof(TimelinePage),
                new PropertyMetadata(null, OnViewModelChanged));

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelinePage page && e.NewValue is TimelineViewModel viewModel)
            {
                page.DataContext = viewModel; // DataContextをViewModelに設定
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelinePage.xaml.cs:OnViewModelChanged",
                    "ViewModel changed",
                    new { hasViewModel = viewModel != null });
                
                // FilmStripViewとTimeRulerViewのViewModelも明示的に設定
                // XAMLのバインディングがタイミングの問題で動作しない場合に備える
                page.UpdateChildViewModels(viewModel);
            }
        }
        
        private void UpdateChildViewModels(TimelineViewModel viewModel)
        {
            // FilmStripViewを検索してViewModelを設定
            var filmStripView = this.FindName("FilmStripView") as FilmStripView;
            if (filmStripView != null)
            {
                filmStripView.ViewModel = viewModel;
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelinePage.xaml.cs:UpdateChildViewModels",
                    "FilmStripView ViewModel set explicitly",
                    new { hasFilmStripViewModel = filmStripView.ViewModel != null });
            }
            
            // TimeRulerViewも同様に設定（必要に応じて）
            var timeRulerView = this.FindName("TimeRulerView") as TimeRulerView;
            if (timeRulerView != null)
            {
                timeRulerView.ViewModel = viewModel;
            }
        }

        public TimelinePage()
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "TimelinePage.xaml.cs:Constructor",
                "TimelinePage constructor called",
                null);
            InitializeComponent();
            DataContextChanged += TimelinePage_DataContextChanged;
            Loaded += TimelinePage_Loaded;
            
            // マウスホイールイベントを購読
            MouseWheel += TimelinePage_MouseWheel;
        }
        
        private void TimelinePage_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Ctrlキーが押されている場合のみズーム
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                if (ViewModel != null)
                {
                    var currentZoom = ViewModel.PixelsPerSecond.Value;
                    var zoomFactor = e.Delta > 0 ? 1.2 : 1.0 / 1.2; // ホイール上で拡大、下で縮小
                    var newZoom = currentZoom * zoomFactor;
                    
                    // ズームレベルの範囲を制限（10～1000 px/s）
                    newZoom = System.Math.Max(10, System.Math.Min(1000, newZoom));
                    
                    ViewModel.SetZoomLevel(newZoom);
                    
                    e.Handled = true; // イベントを処理済みとしてマーク
                }
            }
        }

        private void TimelinePage_Loaded(object sender, RoutedEventArgs e)
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "TimelinePage.xaml.cs:Loaded",
                "TimelinePage loaded",
                new { hasViewModel = ViewModel != null, hasDataContext = DataContext != null });
        }

        private void TimelinePage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "TimelinePage.xaml.cs:DataContextChanged",
                "DataContext changed",
                new { newValueType = e.NewValue?.GetType().Name ?? "null" });
            if (e.NewValue is TimelineViewModel viewModel)
            {
                ViewModel = viewModel;
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelinePage.xaml.cs:DataContextChanged",
                    "ViewModel set from DataContext",
                    new { hasViewModel = ViewModel != null });
            }
        }
    }
}
