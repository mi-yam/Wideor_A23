using System;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Features.Editor
{
    /// <summary>
    /// EditorView.xaml の相互作用ロジック
    /// AvalonEditとAnchorButtonを配置するエディタビュー（Editor機能専用）
    /// </summary>
    public partial class EditorView : UserControl
    {
        /// <summary>
        /// ViewModelプロパティ
        /// </summary>
        public EditorViewModel ViewModel
        {
            get => (EditorViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(EditorViewModel),
                typeof(EditorView),
                new PropertyMetadata(null, OnViewModelChanged));

        private CompositeDisposable? _disposables;

        public EditorView()
        {
            InitializeComponent();
            Loaded += EditorView_Loaded;
            Unloaded += EditorView_Unloaded;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EditorView view)
            {
                view.SubscribeToScrollCoordinator();
            }
        }

        private void EditorView_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToScrollCoordinator();
            SubscribeToTextEditor();
        }

        private void EditorView_Unloaded(object sender, RoutedEventArgs e)
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

            // ScrollCoordinatorにScrollViewerを登録（AvalonEditのScrollViewerを取得）
            var scrollViewer = GetScrollViewer(TextEditorControl);
            if (scrollViewer != null)
            {
                var registration = ViewModel.ScrollCoordinator.RegisterScrollViewer(scrollViewer);
                _disposables.Add(registration);
            }
        }

        private void SubscribeToTextEditor()
        {
            if (ViewModel == null || TextEditorControl == null || _disposables == null)
                return;

            // TextEditorのDocumentとViewModelのTextを同期
            TextEditorControl.TextChanged += (sender, e) =>
            {
                if (ViewModel != null)
                {
                    ViewModel.Text.Value = TextEditorControl.Text;
                }
            };

            // ViewModelのTextが変更されたらTextEditorを更新（外部からの変更時）
            ViewModel.Text
                .Skip(1)
                .Subscribe(text =>
                {
                    if (TextEditorControl.Text != text)
                    {
                        TextEditorControl.Text = text;
                    }
                })
                .AddTo(_disposables);
        }

        /// <summary>
        /// AvalonEditのTextEditorからScrollViewerを取得
        /// </summary>
        private System.Windows.Controls.ScrollViewer? GetScrollViewer(TextEditor textEditor)
        {
            // TextEditorの内部構造からScrollViewerを取得
            // TextEditorはTextAreaを含み、TextAreaはScrollViewerを含む
            var textArea = textEditor?.TextArea;
            if (textArea == null)
                return null;

            // VisualTreeHelperを使用してScrollViewerを検索
            return FindVisualChild<System.Windows.Controls.ScrollViewer>(textArea);
        }

        /// <summary>
        /// VisualTreeから指定された型の子要素を検索
        /// </summary>
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
    }
}
