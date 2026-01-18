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
                new PropertyMetadata(null));

        public TimelinePage()
        {
            InitializeComponent();
            DataContextChanged += TimelinePage_DataContextChanged;
        }

        private void TimelinePage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is TimelineViewModel viewModel)
            {
                ViewModel = viewModel;
            }
        }
    }
}
