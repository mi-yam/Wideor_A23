using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Features.Timeline;
using Wideor.App.Shell;
using Wideor.App.Shared.Domain;
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

        /// <summary>
        /// TimelineViewModelプロパティ（Enterキー分割機能用）
        /// </summary>
        public TimelineViewModel? TimelineViewModel
        {
            get => (TimelineViewModel?)GetValue(TimelineViewModelProperty);
            set => SetValue(TimelineViewModelProperty, value);
        }

        public static readonly DependencyProperty TimelineViewModelProperty =
            DependencyProperty.Register(
                nameof(TimelineViewModel),
                typeof(TimelineViewModel),
                typeof(EditorView),
                new PropertyMetadata(null));


        private CompositeDisposable? _disposables;

        public EditorView()
        {
            InitializeComponent();
            Loaded += EditorView_Loaded;
            Unloaded += EditorView_Unloaded;
            DataContextChanged += EditorView_DataContextChanged;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EditorView view)
            {
                view.SubscribeToScrollCoordinator();
                // ViewModelが変更されたらTextEditorの購読も再設定
                view.SubscribeToTextEditor();

                LogHelper.WriteLog(
                    "EditorView:OnViewModelChanged",
                    "ViewModel changed, subscriptions updated",
                    new { hasViewModel = view.ViewModel != null, hasTextEditor = view.TextEditorControl != null });
            }
        }

        private bool _isLoaded = false;

        private void EditorView_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            EnsureViewModelAssigned();
            SubscribeToScrollCoordinator();
            SubscribeToTextEditor();
            SetupTextEditorSelectionColors();
            SetupEnterKeyHandler();

            LogHelper.WriteLog(
                "EditorView:EditorView_Loaded",
                "EditorView loaded",
                new { hasViewModel = ViewModel != null, hasTextEditor = TextEditorControl != null });
        }

        private void EditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            EnsureViewModelAssigned();
        }

        /// <summary>
        /// Enterキーで動画を分割する機能をセットアップ
        /// 再生中にEnterキーを押すと、現在位置でCUTコマンドを自動挿入
        /// </summary>
        private void SetupEnterKeyHandler()
        {
            if (TextEditorControl == null)
                return;

            TextEditorControl.PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>
        /// キー押下時の処理
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enterキー以外は無視
            if (e.Key != Key.Enter)
                return;

            // TimelineViewModelがない場合は通常のEnter動作
            if (TimelineViewModel == null)
                return;

            // 現在再生中のセグメントを取得
            var currentSegment = TimelineViewModel.CurrentPlayingSegment?.Value;
            if (currentSegment == null)
                return;

            // 再生中の場合のみCUTコマンドを挿入
            if (currentSegment.State == SegmentState.Playing)
            {
                // 現在の再生位置を取得
                var currentPosition = TimelineViewModel.CurrentPosition?.Value ?? 0.0;

                // CUTコマンドを生成
                var cutCommand = FormatCutCommand(currentPosition);

                // 現在のカーソル位置にCUTコマンドを挿入
                var caretOffset = TextEditorControl.CaretOffset;
                TextEditorControl.Document.Insert(caretOffset, cutCommand + Environment.NewLine);

                // カーソル位置を更新
                TextEditorControl.CaretOffset = caretOffset + cutCommand.Length + Environment.NewLine.Length;

                // Enterキーの通常動作をキャンセル
                e.Handled = true;

                LogHelper.WriteLog(
                    "EditorView:OnPreviewKeyDown",
                    "CUT command inserted",
                    new { currentPosition = currentPosition, command = cutCommand });
            }
        }

        /// <summary>
        /// 時間をCUTコマンド形式にフォーマット
        /// </summary>
        private string FormatCutCommand(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return $"CUT {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        /// <summary>
        /// TextEditorの選択色を設定（TextAreaのプロパティを使用）
        /// </summary>
        private void SetupTextEditorSelectionColors()
        {
            if (TextEditorControl?.TextArea != null)
            {
                TextEditorControl.TextArea.SelectionBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x5A, 0xFE));
                TextEditorControl.TextArea.SelectionForeground = System.Windows.Media.Brushes.White;
            }
        }

        private void EditorView_Unloaded(object sender, RoutedEventArgs e)
        {
            _textSubscription?.Dispose();
            _textSubscription = null;
            _disposables?.Dispose();
            _disposables = null;
            _isLoaded = false;
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

        private bool _isUpdatingFromViewModel = false;
        private bool _isUpdatingFromTextEditor = false;
        private bool _textChangedHandlerRegistered = false;
        private CompositeDisposable? _textSubscription;

        private void SubscribeToTextEditor()
        {
            EnsureViewModelAssigned();
            if (ViewModel == null || TextEditorControl == null)
            {
                LogHelper.WriteLog(
                    "EditorView:SubscribeToTextEditor",
                    "Skipped - ViewModel or TextEditorControl is null",
                    new { hasViewModel = ViewModel != null, hasTextEditor = TextEditorControl != null });
                return;
            }

            // 既存のTextSubscriptionを解除
            _textSubscription?.Dispose();
            _textSubscription = new CompositeDisposable();

            // TextChangedイベントハンドラを一度だけ登録
            if (!_textChangedHandlerRegistered)
            {
                TextEditorControl.TextChanged += OnTextEditorTextChanged;
                _textChangedHandlerRegistered = true;
            }

            // カーソル位置変更イベントハンドラを登録
            TextEditorControl.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

            // ViewModelのTextが変更されたらTextEditorを更新（外部からの変更時）
            ViewModel.Text
                .Subscribe(text =>
                {
                    if (_isUpdatingFromTextEditor)
                        return;

                    // UIスレッドで実行
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (text != null && TextEditorControl != null && TextEditorControl.Text != text)
                        {
                            _isUpdatingFromViewModel = true;
                            try
                            {
                                TextEditorControl.Text = text;

                                LogHelper.WriteLog(
                                    "EditorView:SubscribeToTextEditor",
                                    "TextEditor updated from ViewModel",
                                    new { textLength = text?.Length ?? 0 });
                            }
                            finally
                            {
                                _isUpdatingFromViewModel = false;
                            }
                        }
                    }));
                })
                .AddTo(_textSubscription);

            // 初期値を同期
            if (!string.IsNullOrEmpty(ViewModel.Text.Value) && TextEditorControl.Text != ViewModel.Text.Value)
            {
                _isUpdatingFromViewModel = true;
                try
                {
                    TextEditorControl.Text = ViewModel.Text.Value;
                    LogHelper.WriteLog(
                        "EditorView:SubscribeToTextEditor",
                        "Initial sync from ViewModel",
                        new { textLength = ViewModel.Text.Value?.Length ?? 0 });
                }
                finally
                {
                    _isUpdatingFromViewModel = false;
                }
            }

            LogHelper.WriteLog(
                "EditorView:SubscribeToTextEditor",
                "Subscription completed",
                new { hasViewModel = ViewModel != null, hasTextEditor = TextEditorControl != null });
        }

        /// <summary>
        /// カーソル位置変更時にViewModelのCaretPositionを更新
        /// </summary>
        private void OnCaretPositionChanged(object? sender, EventArgs e)
        {
            if (ViewModel == null || TextEditorControl == null)
                return;

            try
            {
                var caretOffset = TextEditorControl.CaretOffset;
                ViewModel.CaretPosition.Value = caretOffset;

                // ログは頻繁に出力されるので、必要時のみ有効化
                // LogHelper.WriteLog(
                //     "EditorView:OnCaretPositionChanged",
                //     "Caret position updated",
                //     new { caretOffset = caretOffset });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "EditorView:OnCaretPositionChanged",
                    "Error updating caret position",
                    new { error = ex.Message });
            }
        }

        private void EnsureViewModelAssigned()
        {
            if (ViewModel != null)
                return;

            // ShellWindowからEditorViewModelを取得して設定（バインディングが間に合わない場合の保険）
            var window = Window.GetWindow(this);
            if (window is ShellWindow shellWindow && shellWindow.ViewModel != null)
            {
                ViewModel = shellWindow.ViewModel.EditorViewModel;
                LogHelper.WriteLog(
                    "EditorView:EnsureViewModelAssigned",
                    "ViewModel assigned from ShellWindow.ViewModel",
                    new { hasViewModel = ViewModel != null });
                return;
            }

            if (window?.DataContext is ShellViewModel shellViewModel)
            {
                ViewModel = shellViewModel.EditorViewModel;
                LogHelper.WriteLog(
                    "EditorView:EnsureViewModelAssigned",
                    "ViewModel assigned from Window.DataContext",
                    new { hasViewModel = ViewModel != null });
                return;
            }

            LogHelper.WriteLog(
                "EditorView:EnsureViewModelAssigned",
                "ViewModel still null",
                new { hasViewModel = ViewModel != null, windowType = window?.GetType().Name ?? "null" });
        }

        private void OnTextEditorTextChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingFromViewModel)
                return;

            if (ViewModel != null && TextEditorControl != null)
            {
                _isUpdatingFromTextEditor = true;
                try
                {
                    ViewModel.Text.Value = TextEditorControl.Text;
                }
                finally
                {
                    _isUpdatingFromTextEditor = false;
                }
            }
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
