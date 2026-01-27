using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fluent;
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

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(ShellViewModel),
                typeof(ShellRibbon),
                new PropertyMetadata(null, OnViewModelChanged));

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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

        private void ShellRibbon_Loaded(object sender, RoutedEventArgs e)
        {
            // #region agent log
            var viewModelType = ViewModel?.GetType().Name ?? "null";
            var dataContextType = DataContext?.GetType().Name ?? "null";
            LogHelper.WriteLog(
                "ShellRibbon.xaml.cs:Loaded",
                "ShellRibbon loaded",
                new { ViewModelType = viewModelType, DataContextType = dataContextType, HasNewProjectCommand = ViewModel?.NewProjectCommand != null });
            // #endregion
            
            // キーボードショートカットの設定（Ctrl+F1でリボン最小化切り替え）
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.PreviewKeyDown += ParentWindow_PreviewKeyDown;
            }
        }

        /// <summary>
        /// キーボードショートカット処理
        /// </summary>
        private void ParentWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+F1でリボンの最小化を切り替え
            if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (MainRibbon != null)
                {
                    MainRibbon.IsMinimized = !MainRibbon.IsMinimized;
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// タブ切り替え時の処理（ログのみ）
        /// </summary>
        private void MainRibbon_SelectedTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainRibbon?.SelectedTabItem == null) return;
            
            var currentIndex = MainRibbon.Tabs.IndexOf(MainRibbon.SelectedTabItem);
            
            LogHelper.WriteLog(
                "ShellRibbon.xaml.cs:SelectedTabChanged",
                "Tab changed",
                new { TabIndex = currentIndex });
        }

        /// <summary>
        /// リボン最小化状態変更時の処理（ログのみ）
        /// </summary>
        private void MainRibbon_IsMinimizedChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var isMinimized = (bool)e.NewValue;
            
            LogHelper.WriteLog(
                "ShellRibbon.xaml.cs:IsMinimizedChanged",
                "Ribbon minimized state changed",
                new { IsMinimized = isMinimized });
        }
    }
}
