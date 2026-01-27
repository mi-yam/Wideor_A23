using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// FFmpegのダウンロードとパス管理を行うクラス
    /// アプリケーション起動時に自動的にFFmpegをダウンロードしてセットアップします。
    /// </summary>
    public static class FFmpegManager
    {
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();
        private static string? _ffmpegPath;

        /// <summary>
        /// FFmpegが利用可能かどうか
        /// </summary>
        public static bool IsAvailable => _isInitialized && !string.IsNullOrEmpty(_ffmpegPath);

        /// <summary>
        /// FFmpegのパス
        /// </summary>
        public static string? FFmpegPath => _ffmpegPath;

        /// <summary>
        /// FFmpegをダウンロードして初期化します。
        /// </summary>
        /// <param name="progress">進捗報告用のコールバック</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>成功した場合true</returns>
        public static async Task<bool> InitializeAsync(
            IProgress<FFmpegDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_isInitialized)
            {
                return true;
            }

            lock (_lock)
            {
                if (_isInitialized)
                {
                    return true;
                }
            }

            try
            {
                // FFmpegのダウンロード先ディレクトリを設定
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var ffmpegDir = Path.Combine(appDataPath, "Wideor", "ffmpeg");
                
                // ディレクトリが存在しない場合は作成
                if (!Directory.Exists(ffmpegDir))
                {
                    Directory.CreateDirectory(ffmpegDir);
                }

                LogHelper.WriteLog(
                    "FFmpegManager:InitializeAsync",
                    "Starting FFmpeg initialization",
                    new { ffmpegDir = ffmpegDir });

                // FFmpegのパスを設定
                FFmpeg.SetExecutablesPath(ffmpegDir);
                _ffmpegPath = ffmpegDir;

                // FFmpegが既に存在するかチェック
                var ffmpegExe = Path.Combine(ffmpegDir, "ffmpeg.exe");
                var ffprobeExe = Path.Combine(ffmpegDir, "ffprobe.exe");

                if (File.Exists(ffmpegExe) && File.Exists(ffprobeExe))
                {
                    LogHelper.WriteLog(
                        "FFmpegManager:InitializeAsync",
                        "FFmpeg already exists",
                        new { ffmpegExe = ffmpegExe });

                    _isInitialized = true;
                    return true;
                }

                // FFmpegをダウンロード
                progress?.Report(new FFmpegDownloadProgress
                {
                    Stage = FFmpegDownloadStage.Downloading,
                    Message = "FFmpegをダウンロード中...",
                    Progress = 0
                });

                LogHelper.WriteLog(
                    "FFmpegManager:InitializeAsync",
                    "Downloading FFmpeg",
                    new { ffmpegDir = ffmpegDir });

                // Xabe.FFmpeg.Downloaderを使用してダウンロード
                await FFmpegDownloader.GetLatestVersion(
                    FFmpegVersion.Official,
                    ffmpegDir,
                    new Progress<ProgressInfo>(info =>
                    {
                        progress?.Report(new FFmpegDownloadProgress
                        {
                            Stage = FFmpegDownloadStage.Downloading,
                            Message = $"FFmpegをダウンロード中... {info.DownloadedBytes / 1024 / 1024}MB",
                            Progress = info.DownloadedBytes > 0 && info.TotalBytes > 0 
                                ? (double)info.DownloadedBytes / info.TotalBytes 
                                : 0
                        });
                    }));

                // ダウンロード完了確認
                if (File.Exists(ffmpegExe))
                {
                    LogHelper.WriteLog(
                        "FFmpegManager:InitializeAsync",
                        "FFmpeg download completed",
                        new { ffmpegExe = ffmpegExe });

                    progress?.Report(new FFmpegDownloadProgress
                    {
                        Stage = FFmpegDownloadStage.Completed,
                        Message = "FFmpegの準備が完了しました",
                        Progress = 1
                    });

                    _isInitialized = true;
                    return true;
                }
                else
                {
                    LogHelper.WriteLog(
                        "FFmpegManager:InitializeAsync",
                        "FFmpeg download failed - file not found",
                        new { ffmpegExe = ffmpegExe });

                    progress?.Report(new FFmpegDownloadProgress
                    {
                        Stage = FFmpegDownloadStage.Error,
                        Message = "FFmpegのダウンロードに失敗しました",
                        Progress = 0
                    });

                    return false;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "FFmpegManager:InitializeAsync",
                    "FFmpeg initialization failed",
                    new { error = ex.Message, stackTrace = ex.StackTrace });

                progress?.Report(new FFmpegDownloadProgress
                {
                    Stage = FFmpegDownloadStage.Error,
                    Message = $"エラー: {ex.Message}",
                    Progress = 0
                });

                return false;
            }
        }

        /// <summary>
        /// FFmpegが使用可能かどうかを確認します（同期版）
        /// </summary>
        public static bool CheckAvailability()
        {
            if (_isInitialized)
            {
                return true;
            }

            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var ffmpegDir = Path.Combine(appDataPath, "Wideor", "ffmpeg");
                var ffmpegExe = Path.Combine(ffmpegDir, "ffmpeg.exe");

                if (File.Exists(ffmpegExe))
                {
                    FFmpeg.SetExecutablesPath(ffmpegDir);
                    _ffmpegPath = ffmpegDir;
                    _isInitialized = true;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// FFmpegの実行ファイルパスを取得
        /// </summary>
        public static string GetFFmpegExecutablePath()
        {
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _ffmpegPath = Path.Combine(appDataPath, "Wideor", "ffmpeg");
            }

            return Path.Combine(_ffmpegPath, "ffmpeg.exe");
        }

        /// <summary>
        /// FFprobeの実行ファイルパスを取得
        /// </summary>
        public static string GetFFprobeExecutablePath()
        {
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _ffmpegPath = Path.Combine(appDataPath, "Wideor", "ffmpeg");
            }

            return Path.Combine(_ffmpegPath, "ffprobe.exe");
        }
    }

    /// <summary>
    /// FFmpegダウンロードの進捗情報
    /// </summary>
    public class FFmpegDownloadProgress
    {
        /// <summary>
        /// 現在のステージ
        /// </summary>
        public FFmpegDownloadStage Stage { get; init; }

        /// <summary>
        /// メッセージ
        /// </summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// 進捗率（0.0～1.0）
        /// </summary>
        public double Progress { get; init; }
    }

    /// <summary>
    /// FFmpegダウンロードのステージ
    /// </summary>
    public enum FFmpegDownloadStage
    {
        /// <summary>チェック中</summary>
        Checking,
        /// <summary>ダウンロード中</summary>
        Downloading,
        /// <summary>展開中</summary>
        Extracting,
        /// <summary>完了</summary>
        Completed,
        /// <summary>エラー</summary>
        Error
    }
}
