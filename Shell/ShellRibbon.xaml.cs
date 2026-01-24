using System.Windows.Controls;
using Wideor.App.Shared.Infra;

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
                // #region agent log
                var viewModelType = e.NewValue?.GetType().Name ?? "null";
                var oldViewModelType = e.OldValue?.GetType().Name ?? "null";
                LogHelper.WriteLog(
                    "ShellRibbon.xaml.cs:OnViewModelChanged",
                    "ViewModel changed",
                    new { OldViewModelType = oldViewModelType, NewViewModelType = viewModelType, HasNewProjectCommand = (e.NewValue as ShellViewModel)?.NewProjectCommand != null, HasLoadVideoCommand = (e.NewValue as ShellViewModel)?.LoadVideoCommand != null });
                // #endregion
                
                // ViewModelが変更されたら、DataContextも更新
                ribbon.DataContext = e.NewValue;
                
                // #region agent log
                LogHelper.WriteLog(
                    "ShellRibbon.xaml.cs:OnViewModelChanged",
                    "DataContext set",
                    new { DataContextType = ribbon.DataContext?.GetType().Name ?? "null" });
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
            var viewModelType = ViewModel?.GetType().Name ?? "null";
            var dataContextType = DataContext?.GetType().Name ?? "null";
            LogHelper.WriteLog(
                "ShellRibbon.xaml.cs:Loaded",
                "ShellRibbon loaded",
                new { ViewModelType = viewModelType, DataContextType = dataContextType, HasNewProjectCommand = ViewModel?.NewProjectCommand != null });
            // #endregion
        }
    }
}
