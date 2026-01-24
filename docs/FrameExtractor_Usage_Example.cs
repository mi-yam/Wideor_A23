using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Wideor.App.Shared.Infra;

namespace Wideor.Examples
{
    /// <summary>
    /// FrameExtractor30fpsの使用例
    /// </summary>
    public class FrameExtractorUsageExample
    {
        /// <summary>
        /// 基本的な使用例：30fpsでフレームを抽出してファイルに保存
        /// </summary>
        public static async Task BasicExampleAsync(string videoFilePath, string outputDirectory)
        {
            var extractor = new FrameExtractor30fps();

            // イベントハンドラを設定
            extractor.FrameExtracted += (bitmap) =>
            {
                // フレームをファイルに保存
                SaveBitmapToFile(bitmap, outputDirectory);
            };

            extractor.ProgressChanged += (progress) =>
            {
                Console.WriteLine($"進捗: {progress:P2}");
            };

            extractor.ErrorOccurred += (error) =>
            {
                Console.WriteLine($"エラー: {error}");
            };

            try
            {
                // フレーム抽出を開始
                await extractor.ExtractFramesAsync(videoFilePath, 1920, 1080);
            }
            finally
            {
                extractor.Dispose();
            }
        }

        /// <summary>
        /// キャンセル可能な使用例
        /// </summary>
        public static async Task CancellableExampleAsync(
            string videoFilePath,
            CancellationToken cancellationToken)
        {
            var extractor = new FrameExtractor30fps();
            var frameCount = 0;

            extractor.FrameExtracted += (bitmap) =>
            {
                frameCount++;
                Console.WriteLine($"フレーム {frameCount} を抽出しました");
                
                // 100フレーム抽出したら停止
                if (frameCount >= 100)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            };

            try
            {
                await extractor.ExtractFramesAsync(
                    videoFilePath,
                    1920,
                    1080,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("フレーム抽出がキャンセルされました");
            }
            finally
            {
                extractor.Dispose();
            }
        }

        /// <summary>
        /// フレームをメモリに保持する例
        /// </summary>
        public static async Task<System.Collections.Generic.List<BitmapSource>> ExtractToMemoryAsync(
            string videoFilePath,
            int maxFrames = 1000)
        {
            var frames = new System.Collections.Generic.List<BitmapSource>();
            var extractor = new FrameExtractor30fps();

            extractor.FrameExtracted += (bitmap) =>
            {
                // フレームをメモリに追加
                frames.Add(bitmap);

                if (frames.Count >= maxFrames)
                {
                    // 最大フレーム数に達したら停止
                    extractor.Dispose();
                }
            };

            try
            {
                await extractor.ExtractFramesAsync(videoFilePath, 1920, 1080);
            }
            finally
            {
                extractor.Dispose();
            }

            return frames;
        }

        /// <summary>
        /// 特定の時間範囲のみ抽出する例
        /// </summary>
        public static async Task ExtractTimeRangeAsync(
            string videoFilePath,
            double startTime,
            double endTime,
            string outputDirectory)
        {
            var extractor = new FrameExtractor30fps();
            var frameCount = 0;

            extractor.FrameExtracted += (bitmap) =>
            {
                // 現在の時間を取得（簡易実装）
                // 実際の実装では、MediaPlayer.Timeを使用
                var currentTime = frameCount / 30.0; // 30fpsを仮定

                if (currentTime >= startTime && currentTime <= endTime)
                {
                    SaveBitmapToFile(bitmap, outputDirectory, $"frame_{frameCount:00000}.jpg");
                }

                if (currentTime > endTime)
                {
                    extractor.Dispose();
                }

                frameCount++;
            };

            try
            {
                await extractor.ExtractFramesAsync(videoFilePath, 1920, 1080);
            }
            finally
            {
                extractor.Dispose();
            }
        }

        private static void SaveBitmapToFile(BitmapSource bitmap, string directory, string? fileName = null)
        {
            if (fileName == null)
            {
                fileName = $"frame_{DateTime.Now:yyyyMMddHHmmssfff}.jpg";
            }

            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

            var filePath = System.IO.Path.Combine(directory, fileName);
            using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
            {
                encoder.Save(fileStream);
            }
        }
    }
}
