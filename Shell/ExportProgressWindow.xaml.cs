using System;
using System.Windows;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Shell
{
    /// <summary>
    /// エクスポート進捗ダイアログ
    /// </summary>
    public partial class ExportProgressWindow : Window
    {
        /// <summary>
        /// キャンセルがリクエストされた時のイベント
        /// </summary>
        public event Action? CancelRequested;

        public ExportProgressWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 進捗を更新
        /// </summary>
        public void UpdateProgress(ExportProgress progress)
        {
            // プログレスバーを更新
            ProgressBar.Value = progress.Progress * 100;
            PercentText.Text = $"{progress.Progress * 100:F0}%";

            // ステータスメッセージを更新
            StatusText.Text = progress.CurrentStep switch
            {
                ExportStep.Preparing => "準備中...",
                ExportStep.ProcessingSegments => "セグメントを処理中...",
                ExportStep.ApplyingOverlays => "テロップを合成中...",
                ExportStep.Concatenating => "動画を結合中...",
                ExportStep.Encoding => "エンコード中...",
                ExportStep.Completed => "完了",
                ExportStep.Error => "エラーが発生しました",
                _ => progress.Message
            };

            // 詳細メッセージがある場合は表示
            if (!string.IsNullOrEmpty(progress.Message) && 
                progress.CurrentStep != ExportStep.Error)
            {
                StatusText.Text = progress.Message;
            }

            // セグメント情報を更新
            SegmentText.Text = $"セグメント: {progress.CurrentSegmentIndex} / {progress.TotalSegments}";

            // 経過時間を更新
            ElapsedText.Text = $"経過時間: {FormatTimeSpan(progress.Elapsed)}";

            // 残り時間を更新
            if (progress.EstimatedRemaining.HasValue)
            {
                RemainingText.Text = $"残り時間: {FormatTimeSpan(progress.EstimatedRemaining.Value)}";
            }
            else
            {
                RemainingText.Text = "残り時間: 計算中...";
            }

            // 完了またはエラーの場合はボタンを変更
            if (progress.CurrentStep == ExportStep.Completed)
            {
                CancelButton.Content = "閉じる";
                CancelButton.IsEnabled = true;
            }
            else if (progress.CurrentStep == ExportStep.Error)
            {
                CancelButton.Content = "閉じる";
                CancelButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// TimeSpanをフォーマット
        /// </summary>
        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        /// <summary>
        /// キャンセルボタンクリック
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (CancelButton.Content.ToString() == "閉じる")
            {
                Close();
            }
            else
            {
                // キャンセル確認
                var result = MessageBox.Show(
                    "動画の書き出しをキャンセルしますか？",
                    "確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    CancelButton.IsEnabled = false;
                    CancelButton.Content = "キャンセル中...";
                    CancelRequested?.Invoke();
                }
            }
        }

        /// <summary>
        /// ウィンドウを閉じる際の処理
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 処理中の場合は閉じるをキャンセルにリダイレクト
            if (CancelButton.Content.ToString() != "閉じる" && CancelButton.IsEnabled)
            {
                e.Cancel = true;
                CancelButton_Click(this, new RoutedEventArgs());
            }
            else
            {
                base.OnClosing(e);
            }
        }
    }
}
