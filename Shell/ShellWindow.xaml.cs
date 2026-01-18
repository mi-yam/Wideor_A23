using System.Windows;
using Fluent;

namespace Wideor.App.Shell
{
    /// <summary>
    /// ShellWindow.xaml の相互作用ロジック
    /// メインウィンドウ（リボンUI + 4カラムレイアウト）
    /// </summary>
    public partial class ShellWindow : RibbonWindow
    {
        /// <summary>
        /// ViewModelプロパティ
        /// </summary>
        public ShellViewModel ViewModel
        {
            get => (ShellViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(ShellViewModel),
                typeof(ShellWindow),
                new PropertyMetadata(null));

        public ShellWindow()
        {
            InitializeComponent();
            DataContextChanged += ShellWindow_DataContextChanged;
        }

        private void ShellWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is ShellViewModel viewModel)
            {
                ViewModel = viewModel;
            }
        }
    }
}
