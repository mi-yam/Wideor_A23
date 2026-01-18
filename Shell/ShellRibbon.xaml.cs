using System.Windows.Controls;

namespace Wideor.App.Shell
{
    /// <summary>
    /// ShellRibbon.xaml の相互作用ロジック
    /// リボンUI（Shell機能専用）
    /// </summary>
    public partial class ShellRibbon : UserControl
    {
        /// <summary>
        /// ViewModelプロパティ
        /// </summary>
        public ShellViewModel ViewModel
        {
            get => (ShellViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly System.Windows.DependencyProperty ViewModelProperty =
            System.Windows.DependencyProperty.Register(
                nameof(ViewModel),
                typeof(ShellViewModel),
                typeof(ShellRibbon),
                new System.Windows.PropertyMetadata(null, OnViewModelChanged));

        private static void OnViewModelChanged(System.Windows.DependencyObject d, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (d is ShellRibbon ribbon)
            {
                // ViewModelが変更されたら、DataContextも更新
                ribbon.DataContext = e.NewValue;
                // #region agent log
                try
                {
                    var viewModelType = e.NewValue?.GetType().Name ?? "null";
                    System.IO.File.AppendAllText(
                        @".cursor\debug.log",
                        $"{{\"timestamp\":{System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ShellRibbon.xaml.cs:OnViewModelChanged\",\"message\":\"ViewModel changed\",\"data\":{{\"ViewModelType\":\"{viewModelType}\",\"HasNewProjectCommand\":{(e.NewValue as ShellViewModel)?.NewProjectCommand != null}}}}}\n");
                }
                catch { }
                // #endregion
            }
        }

        public ShellRibbon()
        {
            InitializeComponent();
            Loaded += ShellRibbon_Loaded;
        }

        private void ShellRibbon_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // #region agent log
            try
            {
                var viewModelType = ViewModel?.GetType().Name ?? "null";
                var dataContextType = DataContext?.GetType().Name ?? "null";
                System.IO.File.AppendAllText(
                    @".cursor\debug.log",
                    $"{{\"timestamp\":{System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ShellRibbon.xaml.cs:Loaded\",\"message\":\"ShellRibbon loaded\",\"data\":{{\"ViewModelType\":\"{viewModelType}\",\"DataContextType\":\"{dataContextType}\",\"HasNewProjectCommand\":{ViewModel?.NewProjectCommand != null}}}}}\n");
            }
            catch { }
            // #endregion
        }
    }
}
