using LibVLCSharp.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// LibVLCSharpを使用して動画から30fpsでフレームを抽出する実装例
    /// </summary>
    public class FrameExtractor30fps : IDisposable
    {
        private const uint BytePerPixel = 4; // RGBA32
        private const double TargetFrameRate = 30.0;
        private const double FrameInterval = 1.0 / TargetFrameRate; // 約0.033秒

        private readonly LibVLC _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Media? _media;

        // メモリマップドファイル管理
        private MemoryMappedFile? _currentMappedFile;
        private MemoryMappedViewAccessor? _currentMappedViewAccessor;
        private readonly object _bufferLock = new object();

        // フレーム処理キュー
        private readonly ConcurrentQueue<FrameData> _framesToProcess = new();
        private readonly CancellationTokenSource _processingCts = new();

        // フレームレート制御
        private DateTime _lastFrameTime = DateTime.MinValue;
        private long _frameCounter = 0;

        // 動画情報
        private uint _videoWidth;
        private uint _videoHeight;
        private uint _pitch;
        private uint _lines;

        // イベント
        public event Action<BitmapSource>? FrameExtracted;
        public event Action<double>? ProgressChanged; // 0.0 - 1.0
        public event Action<string>? ErrorOccurred;

        public FrameExtractor30fps()
        {
            _libVLC = new LibVLC("--intf=dummy", "--vout=dummy", "--quiet", "--no-video-title-show");
        }

        /// <summary>
        /// 動画から30fpsでフレームを抽出します
        /// </summary>
        public async Task ExtractFramesAsync(
            string videoFilePath,
            uint outputWidth = 1920,
            uint outputHeight = 1080,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 動画情報を取得
                await InitializeVideoAsync(videoFilePath, outputWidth, outputHeight, cancellationToken);

                // メディアを作成
                _media = new Media(_libVLC, videoFilePath, FromType.FromPath);
                _media.AddOption(":no-audio");

                // MediaPlayerを作成
                _mediaPlayer = new MediaPlayer(_libVLC);
                _mediaPlayer.SetVideoFormat("RV32", _videoWidth, _videoHeight, _pitch);
                _mediaPlayer.SetVideoCallbacks(Lock, null, Display);

                // フレーム処理タスクを開始
                var processingTask = ProcessFramesAsync(cancellationToken);

                // 再生開始
                _mediaPlayer.Media = _media;
                _mediaPlayer.Play();

                // 処理が完了するまで待機
                await processingTask;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"フレーム抽出エラー: {ex.Message}");
                throw;
            }
        }

        private async Task InitializeVideoAsync(
            string videoFilePath,
            uint outputWidth,
            uint outputHeight,
            CancellationToken cancellationToken)
        {
            // 動画の実際のサイズを取得（簡易実装）
            // 実際の実装では、Media.Parse()を使用してTracksから取得することを推奨
            _videoWidth = outputWidth;
            _videoHeight = outputHeight;

            // 32バイトアライメント
            _pitch = Align(_videoWidth * BytePerPixel);
            _lines = Align(_videoHeight);
        }

        private static uint Align(uint size)
        {
            return (size % 32 == 0) ? size : ((size / 32) + 1) * 32;
        }

        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            lock (_bufferLock)
            {
                try
                {
                    // 新しいメモリマップドファイルを作成
                    _currentMappedFile = MemoryMappedFile.CreateNew(null, _pitch * _lines);
                    _currentMappedViewAccessor = _currentMappedFile.CreateViewAccessor();

                    // VLCにバッファのポインタを渡す
                    Marshal.WriteIntPtr(planes, _currentMappedViewAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle());

                    return IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"Lockコールバックエラー: {ex.Message}");
                    return IntPtr.Zero;
                }
            }
        }

        private void Display(IntPtr opaque, IntPtr picture)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastFrame = _lastFrameTime == DateTime.MinValue 
                ? double.MaxValue 
                : (now - _lastFrameTime).TotalSeconds;

            // 30fps制御: 前回のフレームから約0.033秒経過しているかチェック
            if (timeSinceLastFrame >= FrameInterval)
            {
                lock (_bufferLock)
                {
                    if (_currentMappedFile != null && _currentMappedViewAccessor != null)
                    {
                        // フレームデータをキューに追加
                        var frameData = new FrameData
                        {
                            File = _currentMappedFile,
                            Accessor = _currentMappedViewAccessor,
                            FrameNumber = _frameCounter,
                            Timestamp = now
                        };

                        _framesToProcess.Enqueue(frameData);

                        // 新しいバッファを準備（次のフレーム用）
                        _currentMappedFile = null;
                        _currentMappedViewAccessor = null;

                        _lastFrameTime = now;
                    }
                }
            }
            else
            {
                // 30fpsに達していない場合は、バッファを破棄
                lock (_bufferLock)
                {
                    _currentMappedViewAccessor?.Dispose();
                    _currentMappedFile?.Dispose();
                    _currentMappedFile = null;
                    _currentMappedViewAccessor = null;
                }
            }

            _frameCounter++;
        }

        private async Task ProcessFramesAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_framesToProcess.TryDequeue(out var frameData))
                    {
                        try
                        {
                            var bitmap = await ProcessFrameAsync(frameData, cancellationToken);
                            if (bitmap != null)
                            {
                                FrameExtracted?.Invoke(bitmap);
                            }

                            // 進捗を更新（簡易実装）
                            if (_mediaPlayer != null && _mediaPlayer.Length > 0)
                            {
                                var progress = (double)_mediaPlayer.Time / _mediaPlayer.Length;
                                ProgressChanged?.Invoke(progress);
                            }
                        }
                        finally
                        {
                            // リソースを確実に解放
                            frameData.Accessor?.Dispose();
                            frameData.File?.Dispose();
                        }
                    }
                    else
                    {
                        // キューが空の場合は少し待機
                        await Task.Delay(10, cancellationToken);
                    }

                    // 再生が終了したかチェック
                    if (_mediaPlayer?.State == VLCState.Ended || 
                        _mediaPlayer?.State == VLCState.Stopped)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセル時は正常終了
            }
        }

        private async Task<BitmapSource?> ProcessFrameAsync(
            FrameData frameData,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var image = new Image<Bgra32>((int)(_pitch / BytePerPixel), (int)_lines);
                    using var sourceStream = frameData.File.CreateViewStream();

                    // メモリマップドファイルから画像データを読み込む
                    var mg = image.GetPixelMemoryGroup();
                    for (int i = 0; i < mg.Count; i++)
                    {
                        sourceStream.Read(MemoryMarshal.AsBytes(mg[i].Span));
                    }

                    // 実際のサイズにクロップ
                    image.Mutate(ctx => ctx.Crop((int)_videoWidth, (int)_videoHeight));

                    // WPFのBitmapSourceに変換
                    return ConvertToBitmapSource(image);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"フレーム処理エラー: {ex.Message}");
                    return null;
                }
            }, cancellationToken);
        }

        private BitmapSource ConvertToBitmapSource(Image<Bgra32> image)
        {
            // ImageSharpからWPFのBitmapSourceに変換
            var width = image.Width;
            var height = image.Height;
            var stride = width * 4; // BGRA32 = 4 bytes per pixel
            var pixelData = new byte[stride * height];

            image.CopyPixelDataTo(pixelData);

            return BitmapSource.Create(
                width,
                height,
                96, // DPI X
                96, // DPI Y
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixelData,
                stride);
        }

        public void Dispose()
        {
            _processingCts?.Cancel();

            // キューに残っているフレームをクリーンアップ
            while (_framesToProcess.TryDequeue(out var frameData))
            {
                frameData.Accessor?.Dispose();
                frameData.File?.Dispose();
            }

            lock (_bufferLock)
            {
                _currentMappedViewAccessor?.Dispose();
                _currentMappedFile?.Dispose();
            }

            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _media?.Dispose();
            _libVLC?.Dispose();
            _processingCts?.Dispose();
        }

        private class FrameData
        {
            public MemoryMappedFile File { get; set; } = null!;
            public MemoryMappedViewAccessor Accessor { get; set; } = null!;
            public long FrameNumber { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
