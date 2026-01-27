using System.Windows;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Shell
{
    /// <summary>
    /// FFmpegダウンロード進捗ダイアログ
    /// </summary>
    public partial class FFmpegDownloadWindow : Window
    {
        public FFmpegDownloadWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 進捗を更新
        /// </summary>
        public void UpdateProgress(FFmpegDownloadProgress progress)
        {
            // ステータスメッセージを更新
            StatusText.Text = progress.Stage switch
            {
                FFmpegDownloadStage.Checking => "FFmpegを確認中...",
                FFmpegDownloadStage.Downloading => "FFmpegをダウンロード中...",
                FFmpegDownloadStage.Extracting => "FFmpegを展開中...",
                FFmpegDownloadStage.Completed => "完了",
                FFmpegDownloadStage.Error => "エラーが発生しました",
                _ => progress.Message
            };

            // 詳細メッセージを更新
            if (!string.IsNullOrEmpty(progress.Message))
            {
                DetailText.Text = progress.Message;
            }

            // プログレスバーを更新
            if (progress.Progress > 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = progress.Progress * 100;
            }
            else
            {
                ProgressBar.IsIndeterminate = true;
            }

            // 完了またはエラーの場合
            if (progress.Stage == FFmpegDownloadStage.Completed)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 100;
            }
            else if (progress.Stage == FFmpegDownloadStage.Error)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 0;
            }
        }
    }
}
