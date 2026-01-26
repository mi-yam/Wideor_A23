using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// IThumbnailProviderの実装クラス。
    /// LibVLCSharpを使用して動画からサムネイルを生成します。
    /// </summary>
    public class ThumbnailProvider : IThumbnailProvider, IDisposable
    {
        private const uint BytePerPixel = 4; // RGBA32
        
        private readonly LibVLC _libVLC;
        // MediaPlayerの再利用を避け、各サムネイル生成ごとに新しいインスタンスを作成する
        // （同じMediaPlayerインスタンスの再利用がネイティブクラッシュの原因となるため）
        private readonly Subject<ThumbnailGenerationProgress> _progressSubject = new();
        private readonly Subject<MediaError> _errorsSubject = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1); // LibVLCインスタンスへのアクセスを直列化
        
        // Video Callbacks用のフィールド（各サムネイル生成ごとに使用）
        private MemoryMappedFile? _currentMappedFile;
        private MemoryMappedViewAccessor? _currentMappedViewAccessor;
        private readonly ConcurrentQueue<FrameData> _framesToProcess = new();
        private readonly object _bufferLock = new object();
        private bool _frameReady = false;
        
        // Video Callbacks用のコンテキスト（各サムネイル生成ごとに使用）
        private class CallbackContext
        {
            public uint Pitch { get; set; }
            public uint Lines { get; set; }
        }
        private CallbackContext? _callbackContext;

        public IObservable<ThumbnailGenerationProgress> Progress => _progressSubject.AsObservable();
        public IObservable<MediaError> Errors => _errorsSubject.AsObservable();

        public ThumbnailProvider()
        {
            // LibVLCのインスタンス作成（初期化はApp.xaml.csで実行済み）
            // ヘッドレスモードで動作するようにオプションを設定（ウィンドウを開かない）
            // --intf=dummy: インターフェースなし
            // --vout=dummy: ビデオ出力なし（ウィンドウを開かない）
            // --quiet: ログ出力を抑制
            // --no-video-title-show: ビデオタイトルを表示しない
            _libVLC = new LibVLC("--intf=dummy", "--vout=dummy", "--quiet", "--no-video-title-show");
        }

        public async Task<BitmapSource?> GenerateThumbnailAsync(
            string videoFilePath,
            double timePosition,
            int width = 320,
            int height = 180,
            CancellationToken cancellationToken = default)
        {
            return await GenerateThumbnailAsyncInternal(videoFilePath, timePosition, width, height, cancellationToken, null);
        }
        
        // 内部的な最適化のためのオーバーロード（Durationを再利用する場合）
        private async Task<BitmapSource?> GenerateThumbnailAsyncInternal(
            string videoFilePath,
            double timePosition,
            int width = 320,
            int height = 180,
            CancellationToken cancellationToken = default,
            double? knownDuration = null)
        {
            // #region agent log
            LogHelper.WriteLog(
                "ThumbnailProvider.cs:GenerateThumbnailAsync",
                "GenerateThumbnailAsync called",
                new { videoFilePath, timePosition, width, height, isCancellationRequested = cancellationToken.IsCancellationRequested });
            // #endregion

            if (string.IsNullOrWhiteSpace(videoFilePath))
            {
                ReportError(MediaErrorType.FileNotFound, "ファイルパスが指定されていません。");
                return null;
            }

            if (!File.Exists(videoFilePath))
            {
                ReportError(MediaErrorType.FileNotFound, $"ファイルが見つかりません: {videoFilePath}");
                return null;
            }

            // MediaPlayerへのアクセスを直列化するため、セマフォでロックを取得
            // #region agent log
            LogHelper.WriteLog(
                "ThumbnailProvider.cs:GenerateThumbnailAsync",
                "Waiting for semaphore",
                new { videoFilePath, timePosition });
            // #endregion

            var semaphoreAcquired = false;
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                semaphoreAcquired = true;
                
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailAsync",
                    "Semaphore acquired",
                    new { videoFilePath, timePosition });
                // #endregion
            }
            catch (OperationCanceledException)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailAsync",
                    "Semaphore wait cancelled",
                    new { videoFilePath, timePosition });
                // #endregion
                return null;
            }
            
            try
            {
                // Task.Run内でキャンセルされた場合でもセマフォを解放するため、
                // ConfigureAwait(false)を使用して、Task.Runの完了を待つ
                BitmapSource? result = null;
                try
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailAsync",
                        "Before Task.Run",
                        new { videoFilePath, timePosition });
                    // #endregion
                    
                    try
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync",
                            "Calling Task.Run",
                            new { videoFilePath, timePosition });
                        // #endregion
                        
                        Task<BitmapSource?> taskRunTask;
                        try
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailAsync",
                                "Creating Task.Run",
                                new { videoFilePath, timePosition });
                            // #endregion
                            
                            taskRunTask = Task.Run(async () =>
                    {
                        try
                        {
                            try
                            {
                                // #region agent log
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "Task.Run lambda started",
                                    new { videoFilePath, timePosition });
                                // #endregion
                            }
                            catch (Exception logEx)
                            {
                                // ログ記録で例外が発生した場合でも続行
                                System.Diagnostics.Debug.WriteLine($"LogHelper.WriteLog failed: {logEx.Message}");
                            }

                            try
                            {
                                try
                                {
                                    // #region agent log
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Before cancellation check",
                                        new { videoFilePath, timePosition });
                                    // #endregion
                                }
                                catch (Exception logEx)
                                {
                                    // ログ記録で例外が発生した場合でも続行
                                    System.Diagnostics.Debug.WriteLine($"LogHelper.WriteLog failed: {logEx.Message}");
                                }
                                
                                cancellationToken.ThrowIfCancellationRequested();
                                
                                try
                                {
                                    // #region agent log
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Cancellation check passed",
                                        new { videoFilePath, timePosition });
                                    // #endregion
                                }
                                catch (Exception logEx)
                                {
                                    // ログ記録で例外が発生した場合でも続行
                                    System.Diagnostics.Debug.WriteLine($"LogHelper.WriteLog failed: {logEx.Message}");
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                try
                                {
                                    // #region agent log
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Cancellation requested before processing",
                                        new { videoFilePath, timePosition });
                                    // #endregion
                                }
                                catch (Exception logEx)
                                {
                                    // ログ記録で例外が発生した場合でも続行
                                    System.Diagnostics.Debug.WriteLine($"LogHelper.WriteLog failed: {logEx.Message}");
                                }
                                throw;
                            }
                            catch (Exception ex)
                            {
                                try
                                {
                                    // #region agent log
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Unexpected exception in cancellation check",
                                        new { videoFilePath, timePosition, exceptionType = ex.GetType().Name, message = ex.Message });
                                    // #endregion
                                }
                                catch (Exception logEx)
                                {
                                    // ログ記録で例外が発生した場合でも続行
                                    System.Diagnostics.Debug.WriteLine($"LogHelper.WriteLog failed: {logEx.Message}");
                                }
                                throw;
                            }

                    // 各サムネイル生成ごとに新しいMediaPlayerインスタンスを作成
                    // （同じMediaPlayerインスタンスの再利用がネイティブクラッシュの原因となるため）
                    MediaPlayer? mediaPlayer = null;
                    try
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Creating new MediaPlayer instance",
                            new { videoFilePath, timePosition });
                        // #endregion
                        
                        mediaPlayer = new MediaPlayer(_libVLC);
                        mediaPlayer.Volume = 0; // 音量ミュート
                        
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "MediaPlayer created",
                            new { videoFilePath, timePosition });
                        // #endregion
                        
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Creating Media",
                            new { videoFilePath, timePosition });
                        // #endregion

                        using var media = new Media(_libVLC, videoFilePath, FromType.FromPath);
                        
                        // 動画のサイズを取得するためにParseを実行
                        uint videoWidth = (uint)width;
                        uint videoHeight = (uint)height;
                        try
                        {
                            var parseTask = media.Parse(MediaParseOptions.ParseLocal | MediaParseOptions.FetchLocal, 2000);
                            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                            {
                                await parseTask.WaitAsync(timeoutCts.Token);
                            }
                            
                            // Tracksから動画サイズを取得
                            var tracks = media.Tracks;
                            foreach (var track in tracks)
                            {
                                if (track.TrackType == TrackType.Video)
                                {
                                    videoWidth = (uint)track.Data.Video.Width;
                                    videoHeight = (uint)track.Data.Video.Height;
                                    break;
                                }
                            }
                            
                            // サイズが取得できない場合はデフォルト値を使用
                            if (videoWidth == 0 || videoHeight == 0)
                            {
                                videoWidth = (uint)width;
                                videoHeight = (uint)height;
                            }
                        }
                        catch
                        {
                            // Parseに失敗した場合はデフォルト値を使用
                            videoWidth = (uint)width;
                            videoHeight = (uint)height;
                        }
                        
                        // 32バイトアライメント
                        uint Align(uint size) => (size % 32 == 0) ? size : ((size / 32) + 1) * 32;
                        var pitch = Align(videoWidth * BytePerPixel);
                        var lines = Align(videoHeight);
                        
                        // Video Callbacksを設定（Mediaを設定する前に設定する必要がある）
                        // 各サムネイル生成ごとにフレームキューをクリア（前のフレームが残らないようにする）
                        _frameReady = false;
                        lock (_bufferLock)
                        {
                            // 古いフレームをクリア
                            while (_framesToProcess.TryDequeue(out var oldFrame))
                            {
                                try
                                {
                                    oldFrame.Accessor?.Dispose();
                                    oldFrame.File?.Dispose();
                                }
                                catch { }
                            }
                            
                            _currentMappedFile = null;
                            _currentMappedViewAccessor = null;
                        }
                        
                        _callbackContext = new CallbackContext { Pitch = pitch, Lines = lines };
                        
                        // Videoフォーマットとコールバックを設定（Mediaを設定する前に設定）
                        mediaPlayer.SetVideoFormat("RV32", videoWidth, videoHeight, pitch);
                        mediaPlayer.SetVideoCallbacks(LockCallback, null, DisplayCallback);
                        
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Video Callbacks set",
                            new { videoWidth, videoHeight, pitch, lines, timePosition });
                        // #endregion

                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Setting Media to MediaPlayer",
                            new { videoFilePath, timePosition });
                        // #endregion

                            // Durationを取得（既に取得済みの場合はそれを使用、そうでない場合は取得を試行）
                            // knownDurationは秒単位、media.Durationはミリ秒単位なので統一する
                            var duration = knownDuration.HasValue ? (long)(knownDuration.Value * 1000) : -1L;
                            
                            // #region agent log
                            try
                            {
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "Using duration",
                                    new { videoFilePath, timePosition, duration, durationSeconds = duration > 0 ? duration / 1000.0 : 0, hasKnownDuration = knownDuration.HasValue });
                            }
                            catch { }
                            // #endregion
                            
                            // Durationが不明な場合のみ、メディアから取得を試行
                            if (duration < 0)
                            {
                                // #region agent log
                                try
                                {
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Duration not known, getting from Media",
                                        new { videoFilePath, timePosition });
                                }
                                catch { }
                                // #endregion
                                
                                duration = media.Duration;
                                
                                // Durationが取得できない場合、media.Parseを試行（短いタイムアウト）
                                if (duration < 0)
                                {
                                    // #region agent log
                                    try
                                    {
                                        LogHelper.WriteLog(
                                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                            "Duration not available from Media, trying Parse",
                                            new { videoFilePath, timePosition });
                                    }
                                    catch { }
                                    // #endregion
                                    
                                    try
                                    {
                                        var parseTask = media.Parse(MediaParseOptions.ParseLocal | MediaParseOptions.FetchLocal, 2000);
                                        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                                        {
                                            await parseTask.WaitAsync(timeoutCts.Token);
                                        }
                                        duration = media.Duration;
                                        
                                        // #region agent log
                                        try
                                        {
                                            LogHelper.WriteLog(
                                                "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                                "Media Parse completed",
                                                new { videoFilePath, timePosition, duration, durationSeconds = duration > 0 ? duration / 1000.0 : 0, parseStatus = media.ParsedStatus.ToString() });
                                        }
                                        catch { }
                                        // #endregion
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        // #region agent log
                                        try
                                        {
                                            LogHelper.WriteLog(
                                                "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                                "Media Parse timeout, using MediaPlayer.Length",
                                                new { videoFilePath, timePosition });
                                        }
                                        catch { }
                                        // #endregion
                                        
                                    }
                                }
                            }
                            
                            // MediaPlayerにMediaを設定
                            try
                            {
                                if (mediaPlayer != null)
                                {
                                    mediaPlayer.Media = media;
                                }
                                else
                                {
                                    ReportError(MediaErrorType.Unknown, "MediaPlayerが作成できませんでした");
                                    return null;
                                }
                            }
                            catch (Exception mediaEx)
                            {
                                // #region agent log
                                try
                                {
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Exception setting Media",
                                        new { videoFilePath, timePosition, exceptionType = mediaEx.GetType().Name, message = mediaEx.Message });
                                }
                                catch { }
                                // #endregion
                                ReportError(MediaErrorType.Unknown, $"MediaPlayerにMediaを設定できませんでした: {mediaEx.Message}", mediaEx);
                                return null;
                            }
                            
                            // #region agent log
                            try
                            {
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "Final duration",
                                    new { videoFilePath, timePosition, duration, durationSeconds = duration > 0 ? duration / 1000.0 : 0 });
                            }
                            catch { }
                            // #endregion

                        // 時間位置を検証（Durationが取得できている場合のみ）
                        // durationはミリ秒単位で保持されている
                        var timeMs = (long)(timePosition * 1000);
                        if (timeMs < 0)
                        {
                            ReportError(MediaErrorType.Unknown, $"無効な時間位置です: {timePosition}秒");
                            return null;
                        }
                        
                        if (duration > 0 && timeMs > duration)
                        {
                            ReportError(MediaErrorType.Unknown, $"無効な時間位置です: {timePosition}秒 (動画の長さ: {duration / 1000.0}秒)");
                            return null;
                        }

                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Seeking to time position",
                            new { videoFilePath, timePosition, timeMs });
                        // #endregion

                        // メディアの再生を開始（シーク前に再生を開始する必要がある）
                        try
                        {
                            if (mediaPlayer != null)
                            {
                                // #region agent log
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "Playing media before seek",
                                    new { videoFilePath, timePosition });
                                // #endregion
                                
                                mediaPlayer.Play();
                                
                                // 再生が開始されるまで少し待機
                                await Task.Delay(200, cancellationToken);
                            }
                            else
                            {
                                ReportError(MediaErrorType.Unknown, "MediaPlayerがnullです");
                                return null;
                            }
                        }
                        catch (Exception playEx)
                        {
                            // #region agent log
                            try
                            {
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "Exception playing media",
                                    new { videoFilePath, timePosition, exceptionType = playEx.GetType().Name, message = playEx.Message });
                            }
                            catch { }
                            // #endregion
                            ReportError(MediaErrorType.Unknown, $"再生に失敗しました: {playEx.Message}", playEx);
                            return null;
                        }

                        // 指定時刻にシーク
                        try
                        {
                            if (mediaPlayer != null)
                            {
                                // #region agent log
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "Seeking to time position",
                                    new { videoFilePath, timePosition, timeMs, currentTime = mediaPlayer.Time });
                                // #endregion
                                
                                mediaPlayer.Time = timeMs;
                                
                                // シークが完了してフレームが更新されるまで待機
                                // TimeChangedイベントを待つか、時間が更新されるまで待つ
                                var seekWaitStart = DateTime.UtcNow;
                                var seekWaitTimeout = TimeSpan.FromSeconds(2);
                                var targetTimeMs = timeMs;
                                
                                while (DateTime.UtcNow - seekWaitStart < seekWaitTimeout)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    
                                    var currentTime = mediaPlayer.Time;
                                    // シーク先の時間に近づいたら完了（±100msの許容範囲）
                                    if (Math.Abs(currentTime - targetTimeMs) < 100)
                                    {
                                        // #region agent log
                                        LogHelper.WriteLog(
                                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                            "Seek completed",
                                            new { targetTime = targetTimeMs, currentTime, difference = Math.Abs(currentTime - targetTimeMs) });
                                        // #endregion
                                        break;
                                    }
                                    
                                    await Task.Delay(50, cancellationToken);
                                }
                                
                                // シーク後のフレーム更新を待つ（追加の待機時間）
                                await Task.Delay(500, cancellationToken);
                                
                                // 一時停止してフレームを固定（これにより、指定した時間位置のフレームが確実に取得できる）
                                try
                                {
                                    if (mediaPlayer.State == VLCState.Playing)
                                    {
                                        mediaPlayer.Pause();
                                        await Task.Delay(200, cancellationToken); // 一時停止の完了を待つ
                                        
                                        // #region agent log
                                        LogHelper.WriteLog(
                                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                            "MediaPlayer paused after seek",
                                            new { timePosition, currentTime = mediaPlayer.Time });
                                        // #endregion
                                    }
                                }
                                catch (Exception pauseEx)
                                {
                                    // #region agent log
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Exception pausing after seek",
                                        new { exceptionType = pauseEx.GetType().Name, message = pauseEx.Message });
                                    // #endregion
                                    // 一時停止に失敗しても続行
                                }
                            }
                            else
                            {
                                ReportError(MediaErrorType.Unknown, "MediaPlayerがnullです");
                                return null;
                            }
                        }
                        catch (Exception seekEx)
                        {
                            // #region agent log
                            try
                            {
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "Exception seeking to time position",
                                    new { videoFilePath, timePosition, timeMs, exceptionType = seekEx.GetType().Name, message = seekEx.Message });
                            }
                            catch { }
                            // #endregion
                            ReportError(MediaErrorType.Unknown, $"シークに失敗しました: {seekEx.Message}", seekEx);
                            return null;
                        }

                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Waiting for frame",
                            new { videoFilePath, timePosition });
                        // #endregion

                        // フレームが読み込まれるまで待機
                        await WaitForFrameAsync(mediaPlayer, cancellationToken);

                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "MediaPlayer playing, waiting for DisplayCallback",
                            new { videoFilePath, timePosition, width, height, state = mediaPlayer.State });
                        // #endregion

                        // DisplayCallbackが呼ばれてフレームがキューに追加されるまで待機（最大5秒）
                        var frameWaitStart = DateTime.UtcNow;
                        var frameWaitTimeout = TimeSpan.FromSeconds(5);
                        var lastLogTime = DateTime.UtcNow;
                        
                        while (!_frameReady && !_framesToProcess.Any() && DateTime.UtcNow - frameWaitStart < frameWaitTimeout)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await Task.Delay(100, cancellationToken);
                            
                            // MediaPlayerの状態を確認
                            var state = mediaPlayer.State;
                            if (state == VLCState.Ended || state == VLCState.Error || state == VLCState.Stopped)
                            {
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "MediaPlayer stopped unexpectedly",
                                    new { state = state.ToString() });
                                break;
                            }
                            
                            // 1秒ごとにログを出力（デバッグ用）
                            if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 1.0)
                            {
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "Waiting for DisplayCallback",
                                    new { 
                                        elapsed = (DateTime.UtcNow - frameWaitStart).TotalSeconds,
                                        state = state.ToString(),
                                        frameReady = _frameReady,
                                        queueCount = _framesToProcess.Count
                                    });
                                lastLogTime = DateTime.UtcNow;
                            }
                        }
                        
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Frame wait completed",
                            new { 
                                frameReady = _frameReady,
                                queueCount = _framesToProcess.Count,
                                elapsed = (DateTime.UtcNow - frameWaitStart).TotalSeconds
                            });
                        // #endregion

                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Capturing frame",
                            new { videoFilePath, timePosition, width, height, frameReady = _frameReady, queueCount = _framesToProcess.Count });
                        // #endregion

                        // Video Callbacks方式でフレームを取得
                        var thumbnail = await CaptureFrameWithCallbacksAsync(videoWidth, videoHeight, pitch, lines, width, height, cancellationToken);
                        
                        // Video Callbacksを解除
                        try
                        {
                            mediaPlayer.SetVideoCallbacks(null, null, null);
                        }
                        catch { }

                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Thumbnail captured, disposing MediaPlayer",
                            new { videoFilePath, timePosition, hasThumbnail = thumbnail != null });
                        // #endregion

                        // MediaPlayerを破棄（各サムネイル生成ごとに新しいインスタンスを作成するため）
                        try
                        {
                            if (mediaPlayer != null)
                            {
                                // 安全な破棄手順：
                                // 1. Stop()を呼んで再生を停止
                                // 2. Media = nullを設定
                                // 3. 十分な待機時間を設ける
                                // 4. Dispose()を呼ぶ
                                try
                                {
                                    // 再生を停止
                                    if (mediaPlayer.State == VLCState.Playing || mediaPlayer.State == VLCState.Paused)
                                    {
                                        mediaPlayer.Stop();
                                        await Task.Delay(100, cancellationToken); // Stop完了を待つ
                                    }
                                }
                                catch (Exception stopEx)
                                {
                                    // Stopに失敗しても続行
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Exception stopping MediaPlayer",
                                        new { videoFilePath, timePosition, exceptionType = stopEx.GetType().Name, message = stopEx.Message });
                                }
                                
                                try
                                {
                                    // Mediaをnullに設定
                                    mediaPlayer.Media = null;
                                    await Task.Delay(200, cancellationToken); // Media解放を待つ
                                }
                                catch (Exception mediaEx)
                                {
                                    // Media = nullに失敗しても続行
                                    LogHelper.WriteLog(
                                        "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                        "Exception setting Media to null",
                                        new { videoFilePath, timePosition, exceptionType = mediaEx.GetType().Name, message = mediaEx.Message });
                                }
                                
                                // Dispose()を呼ぶ
                                mediaPlayer.Dispose();
                                
                                // #region agent log
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                    "MediaPlayer disposed",
                                    new { videoFilePath, timePosition });
                                // #endregion
                            }
                        }
                        catch (Exception disposeEx)
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                "Exception disposing MediaPlayer",
                                new { videoFilePath, timePosition, exceptionType = disposeEx.GetType().Name, message = disposeEx.Message, stackTrace = disposeEx.StackTrace });
                            // #endregion
                            // 破棄に失敗しても続行（サムネイルは既に取得済み）
                        }
                        finally
                        {
                            mediaPlayer = null;
                        }

                        return thumbnail;
                    }
                    catch (OperationCanceledException)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Operation cancelled",
                            new { videoFilePath, timePosition });
                        // #endregion
                        // MediaPlayerを安全に破棄
                        try 
                        { 
                            if (mediaPlayer != null)
                            {
                                try
                                {
                                    if (mediaPlayer.State == VLCState.Playing || mediaPlayer.State == VLCState.Paused)
                                    {
                                        mediaPlayer.Stop();
                                        System.Threading.Thread.Sleep(100);
                                    }
                                }
                                catch { }
                                
                                try
                                {
                                    mediaPlayer.Media = null;
                                    System.Threading.Thread.Sleep(100);
                                }
                                catch { }
                                
                                mediaPlayer.Dispose();
                            }
                        } 
                        catch { }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                            "Exception in Task.Run",
                            new { videoFilePath, timePosition, exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, innerException = ex.InnerException?.ToString() });
                        // #endregion
                        // エラー時もMediaPlayerを破棄
                        try { mediaPlayer?.Dispose(); } catch { }
                        ReportError(MediaErrorType.Unknown, $"サムネイル生成に失敗しました: {ex.Message}", ex);
                        return null;
                    }
                        }
                        catch (Exception outerEx)
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailAsync:Task.Run",
                                "Exception in Task.Run outer catch",
                                new { videoFilePath, timePosition, exceptionType = outerEx.GetType().Name, message = outerEx.Message, stackTrace = outerEx.StackTrace, innerException = outerEx.InnerException?.ToString() });
                            // #endregion
                            // エラー時もMediaPlayerの状態をリセット
                            // MediaPlayerは各サムネイル生成ごとに作成・破棄されるため、ここでは何もしない
                            ReportError(MediaErrorType.Unknown, $"サムネイル生成に失敗しました（外側の例外）: {outerEx.Message}", outerEx);
                            return null;
                        }
                    }, cancellationToken);
                        }
                        catch (Exception taskRunCreationEx)
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailAsync",
                                "Exception creating Task.Run",
                                new { videoFilePath, timePosition, exceptionType = taskRunCreationEx.GetType().Name, message = taskRunCreationEx.Message, stackTrace = taskRunCreationEx.StackTrace, innerException = taskRunCreationEx.InnerException?.ToString() });
                            // #endregion
                            // エラー時もMediaPlayerの状態をリセット
                            // MediaPlayerは各サムネイル生成ごとに作成・破棄されるため、ここでは何もしない
                            ReportError(MediaErrorType.Unknown, $"Task.Run作成でエラーが発生しました: {taskRunCreationEx.Message}", taskRunCreationEx);
                            result = null;
                            return result;
                        }
                        
                        try
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailAsync",
                                "Awaiting Task.Run",
                                new { videoFilePath, timePosition });
                            // #endregion
                            
                            result = await taskRunTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException taskRunCancelEx)
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailAsync",
                                "Operation cancelled in Task.Run await",
                                new { videoFilePath, timePosition, exceptionType = taskRunCancelEx.GetType().Name, message = taskRunCancelEx.Message });
                            // #endregion
                            // エラー時もMediaPlayerの状態をリセット
                            // MediaPlayerは各サムネイル生成ごとに作成・破棄されるため、ここでは何もしない
                            result = null;
                            // OperationCanceledExceptionを再スローしない（finallyブロックを実行させるため）
                        }
                        catch (Exception taskRunEx)
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailAsync",
                                "Exception in Task.Run call",
                                new { videoFilePath, timePosition, exceptionType = taskRunEx.GetType().Name, message = taskRunEx.Message, stackTrace = taskRunEx.StackTrace, innerException = taskRunEx.InnerException?.ToString() });
                            // #endregion
                            // エラー時もMediaPlayerの状態をリセット
                            // MediaPlayerは各サムネイル生成ごとに作成・破棄されるため、ここでは何もしない
                            ReportError(MediaErrorType.Unknown, $"Task.Run呼び出しでエラーが発生しました: {taskRunEx.Message}", taskRunEx);
                            result = null;
                        }
                    
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync",
                            "Task.Run completed",
                            new { videoFilePath, timePosition, hasResult = result != null });
                        // #endregion
                        
                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync",
                            "Operation cancelled (inner catch)",
                            new { videoFilePath, timePosition });
                        // #endregion
                        // キャンセル時もMediaPlayerの状態をリセット
                        // MediaPlayerは各サムネイル生成ごとに作成・破棄されるため、ここでは何もしない
                        return null;
                    }
                    catch (Exception innerEx)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync",
                            "Exception (inner catch)",
                            new { videoFilePath, timePosition, exceptionType = innerEx.GetType().Name, message = innerEx.Message, stackTrace = innerEx.StackTrace, innerException = innerEx.InnerException?.ToString() });
                        // #endregion
                        // エラー時もMediaPlayerの状態をリセット
                        // MediaPlayerは各サムネイル生成ごとに作成・破棄されるため、ここでは何もしない
                        ReportError(MediaErrorType.Unknown, $"サムネイル生成中にエラーが発生しました（内側の例外）: {innerEx.Message}", innerEx);
                        return null;
                    }
                }
                catch (OperationCanceledException)
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailAsync",
                        "Operation cancelled (outer catch)",
                        new { videoFilePath, timePosition });
                    // #endregion
                    // キャンセル時もMediaPlayerの状態をリセット
                    SafeCleanupMediaPlayerSync();
                    return null;
                }
                catch (AggregateException aggEx)
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailAsync",
                        "AggregateException caught",
                        new { videoFilePath, timePosition, innerExceptions = aggEx.InnerExceptions?.Select(ex => new { type = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace }).ToArray() });
                    // #endregion
                    // エラー時もMediaPlayerの状態をリセット
                    SafeCleanupMediaPlayerSync();
                    var innerEx = aggEx.InnerException ?? aggEx.InnerExceptions?.FirstOrDefault();
                    ReportError(MediaErrorType.Unknown, $"サムネイル生成中にエラーが発生しました: {innerEx?.Message ?? aggEx.Message}", innerEx ?? aggEx);
                    return null;
                }
                catch (Exception ex)
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailAsync",
                        "Exception (outer catch)",
                        new { videoFilePath, timePosition, exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, innerException = ex.InnerException?.ToString() });
                    // #endregion
                    // エラー時もMediaPlayerの状態をリセット
                    SafeCleanupMediaPlayerSync();
                    ReportError(MediaErrorType.Unknown, $"サムネイル生成中にエラーが発生しました: {ex.Message}", ex);
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailAsync",
                    "Operation cancelled (outermost catch)",
                    new { videoFilePath, timePosition });
                // #endregion
                // キャンセル時もMediaPlayerの状態をリセット
                SafeCleanupMediaPlayerSync();
                return null;
            }
            catch (Exception ex)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailAsync",
                    "Exception (outermost catch)",
                    new { videoFilePath, timePosition, exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, innerException = ex.InnerException?.ToString() });
                // #endregion
                // エラー時もMediaPlayerの状態をリセット
                SafeCleanupMediaPlayerSync();
                ReportError(MediaErrorType.Unknown, $"サムネイル生成中にエラーが発生しました: {ex.Message}", ex);
                return null;
            }
            finally
            {
                // #region agent log
                try
                {
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailAsync",
                        "Finally block entered",
                        new { videoFilePath, timePosition, semaphoreAcquired });
                }
                catch { }
                // #endregion
                
                // セマフォを確実に解放（取得した場合のみ）
                if (semaphoreAcquired)
                {
                    try
                    {
                        _semaphore.Release();
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync",
                            "Semaphore released",
                            new { videoFilePath, timePosition });
                        // #endregion
                    }
                    catch (Exception ex)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailAsync",
                            "Exception releasing semaphore",
                            new { videoFilePath, timePosition, exceptionType = ex.GetType().Name, message = ex.Message });
                        // #endregion
                    }
                }
                else
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailAsync",
                        "Semaphore not acquired, skipping release",
                        new { videoFilePath, timePosition });
                    // #endregion
                }
            }
        }

        public async Task<Dictionary<double, BitmapSource>> GenerateThumbnailsAsync(
            string videoFilePath,
            double[] timePositions,
            int width = 320,
            int height = 180,
            CancellationToken cancellationToken = default,
            double? knownDuration = null)
        {
            // #region agent log
            LogHelper.WriteLog(
                "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                "GenerateThumbnailsAsync called",
                new { videoFilePath, timePositionsCount = timePositions?.Length ?? 0, width, height, isCancellationRequested = cancellationToken.IsCancellationRequested, hasKnownDuration = knownDuration.HasValue, knownDuration = knownDuration });
            // #endregion

            var result = new Dictionary<double, BitmapSource>();

            if (timePositions == null || timePositions.Length == 0)
                return result;

            // Durationを1回だけ取得して、すべてのサムネイル生成で再利用する
            // knownDurationがあればそれを使い、なければ安全に取得する
            double? cachedDuration = null;
            if (knownDuration.HasValue && knownDuration.Value > 0)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                    "Using known duration",
                    new { knownDuration = knownDuration.Value });
                // #endregion
                cachedDuration = knownDuration.Value;
            }
            else
            {
                try
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                        "Getting video duration for reuse",
                        new { videoFilePath });
                    // #endregion
                    
                    cachedDuration = await GetVideoDurationAsync(videoFilePath, cancellationToken);
                    
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                        "Video duration obtained",
                        new { videoFilePath, duration = cachedDuration });
                    // #endregion
                }
                catch (Exception durationEx)
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                        "Failed to get video duration, will try per-thumbnail",
                        new { videoFilePath, exceptionType = durationEx.GetType().Name, message = durationEx.Message });
                    // #endregion
                    // Duration取得に失敗しても続行（各サムネイル生成で個別に試行）
                }
            }

            // セマフォはGenerateThumbnailAsync内で管理されるため、ここでは取得しない
            try
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                    "Starting thumbnail generation loop",
                    new { timePositionsCount = timePositions.Length, hasCachedDuration = cachedDuration.HasValue });
                // #endregion

                for (int i = 0; i < timePositions.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                        "Generating thumbnail",
                        new { index = i, totalCount = timePositions.Length, timePosition = timePositions[i] });
                    // #endregion

                    // 進捗を通知
                    _progressSubject.OnNext(new ThumbnailGenerationProgress
                    {
                        FilePath = videoFilePath,
                        CurrentIndex = i,
                        TotalCount = timePositions.Length
                    });

                    try
                    {
                        var thumbnail = await GenerateThumbnailAsyncInternal(videoFilePath, timePositions[i], width, height, cancellationToken, cachedDuration);
                        if (thumbnail != null)
                        {
                            result[timePositions[i]] = thumbnail;
                            
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                                "Thumbnail generated successfully",
                                new { index = i, timePosition = timePositions[i] });
                            // #endregion
                        }
                        else
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                                "Thumbnail generation returned null",
                                new { index = i, timePosition = timePositions[i] });
                            // #endregion
                        }
                    }
                    catch (Exception ex)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                            "Exception generating thumbnail",
                            new { index = i, timePosition = timePositions[i], exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                        // #endregion
                        // 個別のサムネイル生成エラーは無視して続行
                    }
                }

                // 完了を通知
                _progressSubject.OnNext(new ThumbnailGenerationProgress
                {
                    FilePath = videoFilePath,
                    CurrentIndex = timePositions.Length,
                    TotalCount = timePositions.Length
                });
            }
            catch (OperationCanceledException)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                    "Operation cancelled",
                    null);
                // #endregion
                // キャンセルされた場合は何もしない
            }
            catch (Exception ex)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                    "Exception in GenerateThumbnailsAsync",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, innerException = ex.InnerException?.ToString() });
                // #endregion
                ReportError(MediaErrorType.Unknown, $"複数のサムネイル生成に失敗しました: {ex.Message}", ex);
            }

            // #region agent log
            LogHelper.WriteLog(
                "ThumbnailProvider.cs:GenerateThumbnailsAsync",
                "GenerateThumbnailsAsync completed",
                new { resultCount = result.Count });
            // #endregion

            return result;
        }

        public async Task<Dictionary<double, BitmapSource>> GenerateThumbnailsEvenlyAsync(
            string videoFilePath,
            int count,
            int width = 320,
            int height = 180,
            CancellationToken cancellationToken = default,
            double? knownDuration = null)
        {
            try
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsEvenlyAsync",
                    "GenerateThumbnailsEvenlyAsync called",
                    new { videoFilePath, count, width, height, isCancellationRequested = cancellationToken.IsCancellationRequested, hasKnownDuration = knownDuration.HasValue, knownDuration = knownDuration });
                // #endregion

                if (count <= 0)
                    return new Dictionary<double, BitmapSource>();

                // 動画の長さを取得
                // knownDurationがあればそれを使い、なければ安全に取得する
                double duration;
                if (knownDuration.HasValue && knownDuration.Value > 0)
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailsEvenlyAsync",
                        "Using known duration",
                        new { knownDuration = knownDuration.Value });
                    // #endregion
                    duration = knownDuration.Value;
                }
                else
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GenerateThumbnailsEvenlyAsync",
                        "Getting duration from video file",
                        new { videoFilePath });
                    // #endregion
                    duration = await GetVideoDurationAsync(videoFilePath, cancellationToken);
                }
                
                if (duration <= 0)
                {
                    ReportError(MediaErrorType.Unknown, "動画の長さを取得できませんでした。");
                    return new Dictionary<double, BitmapSource>();
                }

                // 等間隔の時間位置を計算
                var timePositions = new List<double>();
                if (count == 1)
                {
                    timePositions.Add(duration / 2.0); // 中央
                }
                else
                {
                    var interval = duration / (count + 1); // 両端を除く
                    for (int i = 1; i <= count; i++)
                    {
                        timePositions.Add(interval * i);
                    }
                }

                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsEvenlyAsync",
                    "Calling GenerateThumbnailsAsync",
                    new { videoFilePath, timePositionsCount = timePositions.Count, width, height });
                // #endregion

                // knownDurationをGenerateThumbnailsAsyncに渡す
                var result = await GenerateThumbnailsAsync(videoFilePath, timePositions.ToArray(), width, height, cancellationToken, knownDuration);

                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsEvenlyAsync",
                    "GenerateThumbnailsAsync completed",
                    new { resultCount = result.Count });
                // #endregion

                return result;
            }
            catch (OperationCanceledException)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsEvenlyAsync",
                    "Operation cancelled",
                    null);
                // #endregion
                return new Dictionary<double, BitmapSource>();
            }
            catch (Exception ex)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GenerateThumbnailsEvenlyAsync",
                    "Exception in GenerateThumbnailsEvenlyAsync",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, innerException = ex.InnerException?.ToString() });
                // #endregion
                ReportError(MediaErrorType.Unknown, $"等間隔サムネイル生成に失敗しました: {ex.Message}", ex);
                return new Dictionary<double, BitmapSource>();
            }
        }

        public async Task<BitmapSource?> GenerateThumbnailFromImageAsync(
            string imageFilePath,
            int width = 320,
            int height = 180,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageFilePath))
                {
                    ReportError(MediaErrorType.FileNotFound, "ファイルパスが指定されていません。");
                    return null;
                }

                if (!File.Exists(imageFilePath))
                {
                    ReportError(MediaErrorType.FileNotFound, $"ファイルが見つかりません: {imageFilePath}");
                    return null;
                }

                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // WPFのBitmapImageを使用して画像を読み込む
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.UriSource = new Uri(imageFilePath, UriKind.Absolute);
                        bitmapImage.DecodePixelWidth = width;
                        bitmapImage.DecodePixelHeight = height;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();

                        return (BitmapSource)bitmapImage;
                    }
                    catch (Exception ex)
                    {
                        ReportError(MediaErrorType.Unknown, $"画像の読み込みに失敗しました: {ex.Message}", ex);
                        return null;
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"画像サムネイル生成中にエラーが発生しました: {ex.Message}", ex);
                return null;
            }
        }

        private async Task<double> GetVideoDurationAsync(string videoFilePath, CancellationToken cancellationToken)
        {
            // #region agent log
            LogHelper.WriteLog(
                "ThumbnailProvider.cs:GetVideoDurationAsync",
                "Safe GetVideoDurationAsync called",
                new { videoFilePath, isCancellationRequested = cancellationToken.IsCancellationRequested });
            // #endregion

            // GenerateThumbnailAsyncと同様にセマフォでロックして、リソース競合を防ぐ
            var semaphoreAcquired = false;
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                semaphoreAcquired = true;
                
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GetVideoDurationAsync",
                    "Semaphore acquired",
                    new { videoFilePath });
                // #endregion

                // 一時的なMediaPlayerインスタンスを作成してDurationを取得
                Media? media = null;
                MediaPlayer? tempMediaPlayer = null;
                try
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GetVideoDurationAsync",
                        "Creating temporary MediaPlayer",
                        new { videoFilePath });
                    // #endregion
                    
                    tempMediaPlayer = new MediaPlayer(_libVLC);
                    
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GetVideoDurationAsync",
                        "Creating Media",
                        new { videoFilePath });
                    // #endregion

                    media = new Media(_libVLC, videoFilePath, FromType.FromPath);
                    
                    // MediaPlayerにセット
                    try
                    {
                        tempMediaPlayer.Media = media;
                    }
                    catch (Exception mediaEx)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GetVideoDurationAsync",
                            "Exception setting Media",
                            new { videoFilePath, exceptionType = mediaEx.GetType().Name, message = mediaEx.Message });
                        // #endregion
                        return 0;
                    }
                    
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GetVideoDurationAsync",
                        "Media set to MediaPlayer, calling Parse",
                        new { videoFilePath });
                    // #endregion

                    // Parseを試みる（高速、タイムアウト1秒）
                    var status = await media.Parse(MediaParseOptions.ParseLocal | MediaParseOptions.FetchLocal, 1000);
                    
                    long durationMs = media.Duration;
                    
                    // #region agent log
                    try
                    {
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GetVideoDurationAsync",
                            "Parse completed",
                            new { videoFilePath, status = status.ToString(), mediaDuration = media.Duration });
                    }
                    catch { }
                    // #endregion

                    // Parseで取れなければMediaPlayer.Lengthを試す
                    if (durationMs <= 0)
                    {
                        try
                        {
                            // MediaPlayer.Lengthを待つ（最大2秒）
                            var timeout = DateTime.UtcNow.AddSeconds(2);
                            while (tempMediaPlayer != null && tempMediaPlayer.Length <= 0 && DateTime.UtcNow < timeout)
                            {
                                await Task.Delay(100, cancellationToken);
                                try
                                {
                                    if (tempMediaPlayer != null)
                                    {
                                        durationMs = tempMediaPlayer.Length;
                                    }
                                }
                                catch (Exception lengthEx2)
                                {
                                    // #region agent log
                                    try
                                    {
                                        LogHelper.WriteLog(
                                            "ThumbnailProvider.cs:GetVideoDurationAsync",
                                            "Exception getting MediaPlayer.Length in loop",
                                            new { videoFilePath, exceptionType = lengthEx2.GetType().Name, message = lengthEx2.Message });
                                    }
                                    catch { }
                                    // #endregion
                                    break;
                                }
                            }
                        }
                        catch (Exception waitEx)
                        {
                            // #region agent log
                            try
                            {
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:GetVideoDurationAsync",
                                    "Exception waiting for MediaPlayer.Length",
                                    new { videoFilePath, exceptionType = waitEx.GetType().Name, message = waitEx.Message });
                            }
                            catch { }
                            // #endregion
                        }
                    }
                    
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GetVideoDurationAsync",
                        "Duration obtained",
                        new { videoFilePath, durationMs, durationSeconds = durationMs > 0 ? durationMs / 1000.0 : 0 });
                    // #endregion
                    
                    return durationMs > 0 ? durationMs / 1000.0 : 0;
                }
                catch (OperationCanceledException)
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GetVideoDurationAsync",
                        "Operation cancelled",
                        new { videoFilePath });
                    // #endregion
                    return 0;
                }
                catch (Exception ex)
                {
                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:GetVideoDurationAsync",
                        "Exception in GetVideoDurationAsync",
                        new { videoFilePath, exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, innerException = ex.InnerException?.ToString() });
                    // #endregion
                    ReportError(MediaErrorType.Unknown, $"動画の長さ取得に失敗しました: {ex.Message}", ex);
                    return 0;
                }
                finally
                {
                    // リソースをクリーンアップ（安全な破棄手順）
                    try
                    {
                        if (tempMediaPlayer != null)
                        {
                            try
                            {
                                // Stop()を呼んでから破棄
                                if (tempMediaPlayer.State == VLCState.Playing || tempMediaPlayer.State == VLCState.Paused)
                                {
                                    tempMediaPlayer.Stop();
                                    System.Threading.Thread.Sleep(100);
                                }
                            }
                            catch { }
                            
                            try
                            {
                                tempMediaPlayer.Media = null;
                                System.Threading.Thread.Sleep(100);
                            }
                            catch { }
                            
                            tempMediaPlayer.Dispose();
                        }
                    }
                    catch { }
                    
                    try
                    {
                        media?.Dispose();
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GetVideoDurationAsync",
                    "Semaphore wait cancelled",
                    new { videoFilePath });
                // #endregion
                return 0;
            }
            catch (Exception ex)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:GetVideoDurationAsync",
                    "Exception waiting for semaphore",
                    new { videoFilePath, exceptionType = ex.GetType().Name, message = ex.Message });
                // #endregion
                return 0;
            }
            finally
            {
                // セマフォを確実に解放
                if (semaphoreAcquired)
                {
                    try
                    {
                        _semaphore.Release();
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GetVideoDurationAsync",
                            "Semaphore released",
                            new { videoFilePath });
                        // #endregion
                    }
                    catch (Exception ex)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:GetVideoDurationAsync",
                            "Exception releasing semaphore",
                            new { videoFilePath, exceptionType = ex.GetType().Name, message = ex.Message });
                        // #endregion
                    }
                }
            }
        }


        private async Task WaitForFrameAsync(MediaPlayer mediaPlayer, CancellationToken cancellationToken)
        {
            // #region agent log
            LogHelper.WriteLog(
                "ThumbnailProvider.cs:WaitForFrameAsync",
                "WaitForFrameAsync called",
                new { isCancellationRequested = cancellationToken.IsCancellationRequested });
            // #endregion

            try
            {
                // MediaPlayerが再生状態になるまで待機（最大5秒）
                var maxWaitTime = TimeSpan.FromSeconds(5);
                var startTime = DateTime.UtcNow;

                while (DateTime.UtcNow - startTime < maxWaitTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // MediaPlayerの状態を確認
                    var state = mediaPlayer.State;
                    if (state == VLCState.Playing || state == VLCState.Paused)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:WaitForFrameAsync",
                            "MediaPlayer is playing/paused",
                            new { state = state.ToString() });
                        // #endregion

                        // フレームが読み込まれるまで少し待機（Video Callbacksが呼ばれるまで）
                        await Task.Delay(500, cancellationToken);
                        break;
                    }
                    else if (state == VLCState.Ended || state == VLCState.Error || state == VLCState.Stopped)
                    {
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:WaitForFrameAsync",
                            "MediaPlayer stopped unexpectedly",
                            new { state = state.ToString() });
                        // #endregion
                        break;
                    }

                    await Task.Delay(100, cancellationToken);
                }

                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:WaitForFrameAsync",
                    "WaitForFrameAsync completed",
                    new { elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds, finalState = mediaPlayer.State.ToString() });
                // #endregion
            }
            catch (OperationCanceledException)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:WaitForFrameAsync",
                    "Operation cancelled",
                    null);
                // #endregion
                throw;
            }
            catch (Exception ex)
            {
                // #region agent log
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:WaitForFrameAsync",
                    "Exception in WaitForFrameAsync",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                // #endregion
                throw;
            }
        }

        private IntPtr LockCallback(IntPtr opaque, IntPtr planes)
        {
            lock (_bufferLock)
            {
                try
                {
                    // 既存のバッファをクリーンアップ
                    _currentMappedViewAccessor?.Dispose();
                    _currentMappedFile?.Dispose();
                    
                    // 新しいメモリマップドファイルを作成
                    if (_callbackContext == null || _callbackContext.Pitch == 0 || _callbackContext.Lines == 0)
                    {
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:LockCallback",
                            "Invalid callback context",
                            new { hasContext = _callbackContext != null, pitch = _callbackContext?.Pitch ?? 0, lines = _callbackContext?.Lines ?? 0 });
                        return IntPtr.Zero;
                    }
                    
                    var bufferSize = _callbackContext.Pitch * _callbackContext.Lines;
                    _currentMappedFile = MemoryMappedFile.CreateNew(null, bufferSize);
                    _currentMappedViewAccessor = _currentMappedFile.CreateViewAccessor();
                    
                    // VLCにバッファのポインタを渡す
                    Marshal.WriteIntPtr(planes, _currentMappedViewAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle());
                    
                    return IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:LockCallback",
                        "Lock callback error",
                        new { exceptionType = ex.GetType().Name, message = ex.Message });
                    return IntPtr.Zero;
                }
            }
        }
        
        private void DisplayCallback(IntPtr opaque, IntPtr picture)
        {
            try
            {
                lock (_bufferLock)
                {
                    if (_currentMappedFile != null && _currentMappedViewAccessor != null)
                    {
                        // 既にフレームがキューにある場合は、古いフレームを破棄して新しいフレームのみを保持
                        // （これにより、最新のフレームのみが取得される）
                        while (_framesToProcess.Count > 0)
                        {
                            if (_framesToProcess.TryDequeue(out var oldFrame))
                            {
                                try
                                {
                                    oldFrame.Accessor?.Dispose();
                                    oldFrame.File?.Dispose();
                                }
                                catch { }
                            }
                        }
                        
                        // フレームデータをキューに追加
                        var frameData = new FrameData
                        {
                            File = _currentMappedFile,
                            Accessor = _currentMappedViewAccessor
                        };
                        
                        _framesToProcess.Enqueue(frameData);
                        
                        // 新しいバッファを準備（次のフレーム用）
                        _currentMappedFile = null;
                        _currentMappedViewAccessor = null;
                        
                        _frameReady = true;
                        
                        // ログ出力（デバッグ用）
                        try
                        {
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:DisplayCallback",
                                "Frame enqueued",
                                new { queueCount = _framesToProcess.Count });
                        }
                        catch { }
                    }
                    else
                    {
                        // バッファが準備されていない場合のログ
                        try
                        {
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:DisplayCallback",
                                "No buffer available",
                                new { hasFile = _currentMappedFile != null, hasAccessor = _currentMappedViewAccessor != null });
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:DisplayCallback",
                        "Exception in DisplayCallback",
                        new { exceptionType = ex.GetType().Name, message = ex.Message });
                }
                catch { }
            }
        }
        
        private async Task<BitmapSource?> CaptureFrameWithCallbacksAsync(
            uint videoWidth, uint videoHeight, uint pitch, uint lines,
            int outputWidth, int outputHeight, CancellationToken cancellationToken)
        {
            // フレームデータの読み込みは非UIスレッドで実行
            byte[]? pixelData = null;
            int imageWidth = 0;
            int imageHeight = 0;
            
            try
            {
                // フレームがキューに追加されるまで待機（最大3秒）
                var maxWaitTime = TimeSpan.FromSeconds(3);
                var startTime = DateTime.UtcNow;
                
                while (!_frameReady && !_framesToProcess.Any() && DateTime.UtcNow - startTime < maxWaitTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(50, cancellationToken);
                }
                
                // フレームが準備できているか確認
                if (!_framesToProcess.TryDequeue(out var frameData))
                {
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:CaptureFrameWithCallbacksAsync",
                        "No frame available",
                        new { frameReady = _frameReady, queueCount = _framesToProcess.Count });
                    return null;
                }
                
                // メモリマップドファイルから画像データを読み込む（非UIスレッドで実行）
                using (frameData.File)
                using (frameData.Accessor)
                using (var sourceStream = frameData.File.CreateViewStream())
                {
                    pixelData = new byte[(int)(pitch * lines)];
                    var bytesRead = sourceStream.Read(pixelData, 0, pixelData.Length);
                    
                    if (bytesRead != pixelData.Length)
                    {
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:CaptureFrameWithCallbacksAsync",
                            "Incomplete pixel data read",
                            new { expected = pixelData.Length, actual = bytesRead });
                        return null;
                    }
                    
                    // ImageSharpで画像を処理（非UIスレッドで実行）
                    imageWidth = (int)(pitch / BytePerPixel);
                    imageHeight = (int)lines;
                    
                    using var image = Image.LoadPixelData<Bgra32>(pixelData, imageWidth, imageHeight);
                    
                    // 実際のサイズにクロップ
                    image.Mutate(ctx => ctx.Crop((int)videoWidth, (int)videoHeight));
                    
                    // リサイズ（出力サイズに合わせる）
                    if (outputWidth != (int)videoWidth || outputHeight != (int)videoHeight)
                    {
                        image.Mutate(ctx => ctx.Resize(outputWidth, outputHeight));
                    }
                    
                    // ピクセルデータを再取得（リサイズ後）
                    var finalPixelData = new byte[outputWidth * outputHeight * 4];
                    image.CopyPixelDataTo(finalPixelData);
                    pixelData = finalPixelData;
                    imageWidth = outputWidth;
                    imageHeight = outputHeight;
                }
                
                // BitmapSourceの作成はUIスレッドで実行（またはFreeze()でスレッドセーフにする）
                // ここではFreeze()を使用する方法を採用（UIスレッドへの切り替えは不要）
                return await Task.Run(() =>
                {
                    try
                    {
                        var bitmapSource = BitmapSource.Create(
                            imageWidth,
                            imageHeight,
                            96, // DPI X
                            96, // DPI Y
                            System.Windows.Media.PixelFormats.Bgra32,
                            null,
                            pixelData,
                            imageWidth * 4); // stride
                        
                        // Freeze()を呼んでスレッドセーフにする（これにより、非UIスレッドから作成してもUIスレッドで使用可能になる）
                        bitmapSource.Freeze();
                        
                        return bitmapSource;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:CaptureFrameWithCallbacksAsync",
                            "Failed to create BitmapSource",
                            new { errorMessage = ex.Message, stackTrace = ex.StackTrace });
                        return null;
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "ThumbnailProvider.cs:CaptureFrameWithCallbacksAsync",
                    "Failed to capture frame",
                    new { errorMessage = ex.Message, stackTrace = ex.StackTrace });
                return null;
            }
        }
        
        [Obsolete("Use direct BitmapSource creation in CaptureFrameWithCallbacksAsync instead")]
        private BitmapSource ConvertToBitmapSource(Image<Bgra32> image)
        {
            var width = image.Width;
            var height = image.Height;
            var stride = width * 4; // BGRA32 = 4 bytes per pixel
            var pixelData = new byte[stride * height];
            
            image.CopyPixelDataTo(pixelData);
            
            // BitmapSourceを作成（非UIスレッドで作成される可能性があるため、Freeze()でスレッドセーフにする）
            var bitmapSource = BitmapSource.Create(
                width,
                height,
                96, // DPI X
                96, // DPI Y
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixelData,
                stride);
            
            // Freeze()を呼んでスレッドセーフにする（これにより、非UIスレッドから作成してもUIスレッドで使用可能になる）
            bitmapSource.Freeze();
            
            return bitmapSource;
        }
        
        private class FrameData
        {
            public MemoryMappedFile File { get; set; } = null!;
            public MemoryMappedViewAccessor Accessor { get; set; } = null!;
        }
        
        [Obsolete("Use CaptureFrameWithCallbacksAsync instead")]
        private async Task<BitmapSource?> CaptureFrameAsync(MediaPlayer mediaPlayer, int width, int height, CancellationToken cancellationToken)
        {
            // #region agent log
            LogHelper.WriteLog(
                "ThumbnailProvider.cs:CaptureFrameAsync",
                "CaptureFrameAsync called",
                new { width, height, isCancellationRequested = cancellationToken.IsCancellationRequested });
            // #endregion

            return await Task.Run(() =>
            {
                try
                {
                    // LibVLCSharpでフレームをキャプチャする方法
                    // TakeSnapshotメソッドを使用（非推奨の可能性があるが、現時点で利用可能な方法）
                    // 一時ファイルに保存してから読み込む
                    var tempPath = Path.Combine(Path.GetTempPath(), $"thumbnail_{Guid.NewGuid()}.png");

                    // #region agent log
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:CaptureFrameAsync",
                        "Taking snapshot",
                        new { tempPath, width, height });
                    // #endregion

                    try
                    {
                        // TakeSnapshotは非同期で動作するため、同期的に待機する必要がある
                        // MediaPlayerがPlayingまたはPaused状態であることを確認
                        if (mediaPlayer.State != VLCState.Playing && mediaPlayer.State != VLCState.Paused)
                        {
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:CaptureFrameAsync",
                                "MediaPlayer not in valid state for snapshot",
                                new { state = mediaPlayer.State.ToString() });
                            return null;
                        }
                        
                        mediaPlayer.TakeSnapshot(0, tempPath, (uint)width, (uint)height);

                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:CaptureFrameAsync",
                            "TakeSnapshot called, waiting for file",
                            new { tempPath });
                        // #endregion

                        // スナップショットの生成を待つ（最大3秒、より長い待機時間）
                        var maxWaitTime = TimeSpan.FromSeconds(3);
                        var startTime = DateTime.UtcNow;
                        var lastFileSize = 0L;
                        var stableCount = 0;
                        
                        while (DateTime.UtcNow - startTime < maxWaitTime)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            if (File.Exists(tempPath))
                            {
                                var currentFileSize = new FileInfo(tempPath).Length;
                                
                                // ファイルサイズが安定するまで待つ（書き込み完了を確認）
                                if (currentFileSize == lastFileSize && currentFileSize > 0)
                                {
                                    stableCount++;
                                    if (stableCount >= 3) // 3回連続で同じサイズなら完了とみなす
                                    {
                                        break;
                                    }
                                }
                                else
                                {
                                    stableCount = 0;
                                    lastFileSize = currentFileSize;
                                }
                            }
                            
                            System.Threading.Thread.Sleep(100); // 100ms間隔でチェック
                        }

                        if (!File.Exists(tempPath))
                        {
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:CaptureFrameAsync",
                                "Snapshot file was not created",
                                new { tempPath });
                            return null;
                        }

                        // 画像を読み込む
                        // #region agent log
                        LogHelper.WriteLog(
                            "ThumbnailProvider.cs:CaptureFrameAsync",
                            "Loading BitmapImage",
                            new { tempPath, fileExists = File.Exists(tempPath), fileSize = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0 });
                        // #endregion
                        
                        // 一時ファイルを読み込む前にファイル属性を設定（読み取り専用などで開かないようにする）
                        var fileInfo = new FileInfo(tempPath);
                        if (fileInfo.Exists)
                        {
                            fileInfo.Attributes = FileAttributes.Temporary | FileAttributes.NotContentIndexed;
                        }
                        
                        var bitmapImage = new BitmapImage();
                        BitmapSource? result = null;
                        try
                        {
                            // Streamを使用してファイルを読み込む（UriSourceを使わないことで自動起動を防ぐ）
                            using (var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                bitmapImage.BeginInit();
                                bitmapImage.StreamSource = fileStream;
                                bitmapImage.DecodePixelWidth = width;
                                bitmapImage.DecodePixelHeight = height;
                                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                bitmapImage.EndInit();
                                bitmapImage.Freeze();
                                
                                // #region agent log
                                LogHelper.WriteLog(
                                    "ThumbnailProvider.cs:CaptureFrameAsync",
                                    "BitmapImage loaded successfully",
                                    new { pixelWidth = bitmapImage.PixelWidth, pixelHeight = bitmapImage.PixelHeight });
                                // #endregion
                                
                                result = (BitmapSource)bitmapImage;
                            } // fileStreamが閉じられる
                            
                            // ストリームが閉じられた後、ファイルを削除
                            try
                            {
                                File.Delete(tempPath);
                            }
                            catch
                            {
                                // 削除に失敗しても続行
                            }
                        }
                        catch (Exception imageEx)
                        {
                            // #region agent log
                            LogHelper.WriteLog(
                                "ThumbnailProvider.cs:CaptureFrameAsync",
                                "BitmapImage load failed",
                                new { exceptionType = imageEx.GetType().Name, message = imageEx.Message, stackTrace = imageEx.StackTrace });
                            // #endregion
                            throw;
                        }

                        return result;
                    }
                    catch
                    {
                        // 一時ファイルが存在する場合、削除を試みる
                        try
                        {
                            if (File.Exists(tempPath))
                                File.Delete(tempPath);
                        }
                        catch
                        {
                            // 削除に失敗しても続行
                        }
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLog(
                        "ThumbnailProvider.cs:CaptureFrameAsync",
                        "Failed to capture frame",
                        new { errorMessage = ex.Message, stackTrace = ex.StackTrace });
                    return null;
                }
            }, cancellationToken);
        }

        private void ReportError(MediaErrorType errorType, string message, Exception? exception = null)
        {
            var error = new MediaError
            {
                Id = Guid.NewGuid().ToString(),
                ErrorType = errorType,
                Message = message,
                InnerException = exception?.ToString(),
                StackTrace = exception?.StackTrace,
                Severity = MediaErrorSeverity.Error,
                OccurredAt = DateTime.UtcNow
            };

            _errorsSubject.OnNext(error);

            LogHelper.WriteLog(
                "ThumbnailProvider.cs",
                "Error reported",
                new { errorType = errorType.ToString(), message = message, exception = exception?.ToString() });
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
            _progressSubject?.OnCompleted();
            _progressSubject?.Dispose();
            _errorsSubject?.OnCompleted();
            _errorsSubject?.Dispose();
            // LibVLCを破棄（MediaPlayerは各サムネイル生成ごとに作成・破棄されるため、ここでは不要）
            _libVLC?.Dispose();
        }

        // SafeCleanupMediaPlayerSync メソッドを追加
        private void SafeCleanupMediaPlayerSync()
        {
            // このメソッドは、MediaPlayerの同期的なクリーンアップを行うためのプレースホルダーです。
            // 現状、MediaPlayerは各サムネイル生成ごとに作成・破棄されているため、ここでは何もしません。
            // 必要に応じてリソース解放処理を追加してください。
        }
    }
}