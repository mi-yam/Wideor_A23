using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Reactive.Bindings;

namespace Wideor.App.Features.Editor
{
    /// <summary>
    /// AnchorButton.xaml の相互作用ロジック
    /// スマートアンカー専用のボタンUI（Editor機能専用）
    /// </summary>
    public partial class AnchorButton : UserControl
    {
        /// <summary>
        /// 未確定状態かどうか
        /// </summary>
        public bool IsPending
        {
            get => (bool)GetValue(IsPendingProperty);
            set => SetValue(IsPendingProperty, value);
        }

        public static readonly DependencyProperty IsPendingProperty =
            DependencyProperty.Register(
                nameof(IsPending),
                typeof(bool),
                typeof(AnchorButton),
                new PropertyMetadata(false));

        /// <summary>
        /// 確定状態かどうか
        /// </summary>
        public bool IsConfirmed
        {
            get => (bool)GetValue(IsConfirmedProperty);
            set => SetValue(IsConfirmedProperty, value);
        }

        public static readonly DependencyProperty IsConfirmedProperty =
            DependencyProperty.Register(
                nameof(IsConfirmed),
                typeof(bool),
                typeof(AnchorButton),
                new PropertyMetadata(false));

        /// <summary>
        /// クリックコマンド
        /// </summary>
        public ICommand ClickCommand
        {
            get => (ICommand)GetValue(ClickCommandProperty);
            set => SetValue(ClickCommandProperty, value);
        }

        public static readonly DependencyProperty ClickCommandProperty =
            DependencyProperty.Register(
                nameof(ClickCommand),
                typeof(ICommand),
                typeof(AnchorButton),
                new PropertyMetadata(null));

        /// <summary>
        /// ツールチップテキスト
        /// </summary>
        public string ToolTipText
        {
            get => (string)GetValue(ToolTipTextProperty);
            set => SetValue(ToolTipTextProperty, value);
        }

        public static readonly DependencyProperty ToolTipTextProperty =
            DependencyProperty.Register(
                nameof(ToolTipText),
                typeof(string),
                typeof(AnchorButton),
                new PropertyMetadata("アンカー"));

        public AnchorButton()
        {
            InitializeComponent();
        }
    }
}
