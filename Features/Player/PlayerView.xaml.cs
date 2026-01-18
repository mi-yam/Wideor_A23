using System.Windows;
using System.Windows.Controls;

namespace Wideor.App.Features.Player
{
    /// <summary>
    /// PlayerView.xaml の相互作用ロジック
    /// </summary>
    public partial class PlayerView : UserControl
    {
        /// <summary>
        /// ViewModelプロパティ（DataContextとして使用）
        /// </summary>
        public PlayerViewModel ViewModel
        {
            get => (PlayerViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(PlayerViewModel),
                typeof(PlayerView),
                new PropertyMetadata(null));

        public PlayerView()
        {
            InitializeComponent();
        }
    }
}
