using System.Windows;
using Fluent;
using Wideor.App.Shared.Infra;

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
                new PropertyMetadata(null, OnViewModelChanged));

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ShellWindow window && e.NewValue is ShellViewModel viewModel)
            {
                window.DataContext = viewModel; // DataContextをViewModelに設定
                
                // ShellRibbonのViewModelも直接設定
                if (window.FindName("ShellRibbonControl") is ShellRibbon ribbon)
                {
                    ribbon.ViewModel = viewModel;
                }
                
                
                // #region agent log
                LogHelper.WriteLog(
                    "ShellWindow.xaml.cs:OnViewModelChanged",
                    "ViewModel changed",
                    new { hasViewModel = viewModel != null, hasLoadVideoCommand = viewModel?.LoadVideoCommand != null, hasPlayerViewModel = viewModel?.PlayerViewModel != null });
                // #endregion
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        public ShellWindow()
        {
            // #region agent log
            LogHelper.WriteLog(
                "ShellWindow.xaml.cs:Constructor",
                "ShellWindow constructor called",
                new { hasViewModel = ViewModel != null });
            // #endregion
            
            InitializeComponent();
            
            // #region agent log
            LogHelper.WriteLog(
                "ShellWindow.xaml.cs:Constructor",
                "After InitializeComponent",
                new { hasViewModel = ViewModel != null, hasDataContext = DataContext != null });
            // #endregion
            
            DataContextChanged += ShellWindow_DataContextChanged;
            Loaded += ShellWindow_Loaded;
        }

        private void ShellWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // LoadedイベントでShellRibbonのViewModelを確実に設定
            if (ViewModel != null)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ShellWindow.xaml.cs:Loaded",
                    "ShellWindow loaded",
                    new { hasViewModel = ViewModel != null, hasLoadVideoCommand = ViewModel?.LoadVideoCommand != null, hasPlayerViewModel = ViewModel?.PlayerViewModel != null });
                // #endregion
                
                // ShellRibbonを検索してViewModelを設定
                var ribbon = this.FindName("ShellRibbonControl") as ShellRibbon;
                if (ribbon != null)
                {
                    ribbon.ViewModel = ViewModel;
                    // #region agent log
                    LogHelper.WriteLog(
                        "ShellWindow.xaml.cs:Loaded",
                        "ShellRibbon ViewModel set",
                        new { ribbonViewModel = ribbon.ViewModel != null });
                    // #endregion
                }
                
            }
        }

        private void ShellWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is ShellViewModel viewModel)
            {
                ViewModel = viewModel;
                // #region agent log
                LogHelper.WriteLog(
                    "ShellWindow.xaml.cs:DataContextChanged",
                    "ViewModel set",
                    new { hasViewModel = ViewModel != null, hasLoadVideoCommand = ViewModel?.LoadVideoCommand != null });
                // #endregion
            }
        }
    }
}
