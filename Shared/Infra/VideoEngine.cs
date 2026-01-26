using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// IVideoEngineの実装クラス。
    /// LibVLCSharpを使用して動画の再生を提供します。
    /// </summary>
    public class VideoEngine : IVideoEngine, IDisposable
    {
        private readonly LibVLC _libVLC;
        private MediaPlayer? _mediaPlayer;
        private readonly BehaviorSubject<double> _currentPosition = new(0.0);
        private readonly BehaviorSubject<double> _totalDuration = new(0.0);
        private readonly BehaviorSubject<bool> _isPlaying = new(false);
        private readonly BehaviorSubject<bool> _isLoaded = new(false);
        private readonly Subject<MediaError> _errors = new();
        private readonly CompositeDisposable _disposables = new();
        private readonly DispatcherTimer _positionUpdateTimer;
        private double _volume = 1.0;
        private double _playbackSpeed = 1.0;

        public MediaPlayer? MediaPlayer => _mediaPlayer;

        public IObservable<double> CurrentPosition => _currentPosition.AsObservable();
        public IObservable<double> TotalDuration => _totalDuration.AsObservable();
        public IObservable<bool> IsPlaying => _isPlaying.AsObservable();
        public IObservable<bool> IsLoaded => _isLoaded.AsObservable();
        public IObservable<MediaError> Errors => _errors.AsObservable();
        
        /// <summary>
        /// 動画の総時間の現在値（秒）
        /// </summary>
        public double CurrentTotalDuration => _totalDuration.Value;
        
        /// <summary>
        /// 動画の読み込み状態の現在値
        /// </summary>
        public bool CurrentIsLoaded => _isLoaded.Value;

        public VideoEngine()
        {
            // LibVLCのインスタンス作成（初期化はApp.xaml.csで実行済み）
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);

            // 位置更新タイマー（100ms間隔）
            _positionUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionUpdateTimer.Tick += PositionUpdateTimer_Tick;
            _positionUpdateTimer.Start();

            // イベントハンドラの設定
            _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            _mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
            _mediaPlayer.Playing += MediaPlayer_Playing;
            _mediaPlayer.Paused += MediaPlayer_Paused;
            _mediaPlayer.Stopped += MediaPlayer_Stopped;
            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;

            // リソース管理
            _disposables.Add(_currentPosition);
            _disposables.Add(_totalDuration);
            _disposables.Add(_isPlaying);
            _disposables.Add(_isLoaded);
            _disposables.Add(_errors);
        }

        public async Task<bool> LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    ReportError(MediaErrorType.FileNotFound, "ファイルパスが指定されていません。");
                    return false;
                }

                if (!System.IO.File.Exists(filePath))
                {
                    ReportError(MediaErrorType.FileNotFound, $"ファイルが見つかりません: {filePath}");
                    return false;
                }

                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:LoadAsync",
                    "Starting video load",
                    new { filePath = filePath, hasMediaPlayer = _mediaPlayer != null });

                // Ensure MediaPlayer is created on UI thread
                if (_mediaPlayer == null)
                {
                    if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            _mediaPlayer = new MediaPlayer(_libVLC);
                            SetupMediaPlayerEvents();
                        });
                    }
                    else
                    {
                        _mediaPlayer = new MediaPlayer(_libVLC);
                        SetupMediaPlayerEvents();
                    }
                }

                // Create Media and assign to MediaPlayer on UI thread
                Media media = null;
                if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        media = new Media(_libVLC, filePath, FromType.FromPath);
                        _mediaPlayer.Media = media;
                    });
                }
                else
                {
                    media = new Media(_libVLC, filePath, FromType.FromPath);
                    _mediaPlayer.Media = media;
                }

                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:LoadAsync",
                    "Media set",
                    new { filePath = filePath, mediaDuration = media.Duration, playerLength = _mediaPlayer.Length });

                // Media.Parse()をバックグラウンドスレッドで実行（UIスレッドでawaitするとデッドロックが発生する可能性がある）
                try
                {
                    await Task.Run(async () =>
                    {
                        try
                        {
                            await media.Parse(MediaParseOptions.ParseLocal | MediaParseOptions.FetchLocal,5000);
                        }
                        catch (Exception parseEx)
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "VideoEngine.cs:LoadAsync",
                                "Exception in Media.Parse",
                                new { filePath = filePath, exceptionType = parseEx.GetType().Name, message = parseEx.Message });
                            // Parseに失敗しても続行（LengthChangedイベントでDurationを取得できる可能性がある）
                        }
                    });
                }
                catch (Exception parseTaskEx)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:LoadAsync",
                        "Exception awaiting Media.Parse task",
                        new { filePath = filePath, exceptionType = parseTaskEx.GetType().Name, message = parseTaskEx.Message });
                    // Parseに失敗しても続行（LengthChangedイベントでDurationを取得できる可能性がある）
                }

                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:LoadAsync",
                    "Media Parse completed",
                    new { filePath = filePath, parsedStatus = media.ParsedStatus.ToString(), mediaDuration = media.Duration, playerLength = _mediaPlayer.Length });

                // Media.Parse()後は、Media.Durationから直接取得できる
                //ただし、UIスレッドで確認する必要がある
                double duration =0;
                if (media.Duration >0)
                {
                    duration = media.Duration /1000.0; // ミリ秒から秒に変換
                    _totalDuration.OnNext(duration);

                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:LoadAsync",
                        "Media parsed, TotalDuration updated from Media.Duration",
                        new { filePath = filePath, mediaDuration = media.Duration, totalDuration = duration });
                }
                else if (_mediaPlayer.Length >0)
                {
                    // MediaPlayer.Lengthが既に取得できている場合
                    duration = _mediaPlayer.Length /1000.0; // ミリ秒から秒に変換
                    _totalDuration.OnNext(duration);

                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:LoadAsync",
                        "TotalDuration updated from MediaPlayer.Length",
                        new { filePath = filePath, totalDuration = duration });
                }
                else
                {
                    // Durationが取得できない場合、LengthChangedイベントを待つ（最大3秒）
                    // LengthChangedイベントは既にSetupMediaPlayerEvents()で購読されているため、
                    // イベントが発火するまで短時間待機する
                    var timeout = DateTime.Now.AddSeconds(3);
                    var waitCount =0;

                    while (_mediaPlayer.Length == -1 && DateTime.Now < timeout && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(50, cancellationToken); //50ms間隔でチェック
                        waitCount++;
                    }

                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:LoadAsync",
                        "After waiting for MediaPlayer.Length",
                        new { filePath = filePath, length = _mediaPlayer.Length, waitCount = waitCount, timedOut = _mediaPlayer.Length == -1 });

                    if (_mediaPlayer.Length >0)
                    {
                        duration = _mediaPlayer.Length /1000.0; // ミリ秒から秒に変換
                        _totalDuration.OnNext(duration);

                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "VideoEngine.cs:LoadAsync",
                            "TotalDuration updated from MediaPlayer.Length after wait",
                            new { filePath = filePath, totalDuration = duration });
                    }
                    else
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "VideoEngine.cs:LoadAsync",
                            "Duration not available (will be updated by LengthChanged event)",
                            new { filePath = filePath, mediaDuration = media.Duration, playerLength = _mediaPlayer.Length });

                        // Durationが取得できない場合でも、読み込みは成功とする
                        // LengthChangedイベントで後から更新される
                    }
                }

                _isLoaded.OnNext(true);

                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:LoadAsync",
                    "LoadAsync completed successfully",
                    new { filePath = filePath, hasMediaPlayer = _mediaPlayer != null, hasMedia = _mediaPlayer?.Media != null, isLoaded = _isLoaded.Value, duration = duration });

                return true;
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:LoadAsync",
                    "Exception in LoadAsync",
                    new { filePath = filePath, exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, innerException = ex.InnerException?.ToString() });

                ReportError(MediaErrorType.Unknown, $"動画の読み込みに失敗しました: {ex.Message}", ex);
                _isLoaded.OnNext(false);
                return false;
            }
        }

        public void Play()
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:Play",
                        "Cannot play - MediaPlayer is null",
                        null);
                    return;
                }
                
                if (!_isLoaded.Value)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:Play",
                        "Cannot play - Video not loaded",
                        new { isLoaded = _isLoaded.Value });
                    return;
                }
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Play",
                    "Starting playback",
                    new { currentTime = _mediaPlayer.Time / 1000.0, totalDuration = CurrentTotalDuration });
                
                _mediaPlayer.Play();
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Play",
                    "Play command executed",
                    new { isPlaying = _mediaPlayer.IsPlaying });
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Play",
                    "Exception in Play",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                
                ReportError(MediaErrorType.Unknown, $"再生に失敗しました: {ex.Message}", ex);
            }
        }

        public void Pause()
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:Pause",
                        "Cannot pause - MediaPlayer is null",
                        null);
                    return;
                }
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Pause",
                    "Pausing playback",
                    new { currentTime = _mediaPlayer.Time / 1000.0 });
                
                _mediaPlayer.Pause();
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Pause",
                    "Pause command executed",
                    new { isPlaying = _mediaPlayer.IsPlaying });
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Pause",
                    "Exception in Pause",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                
                ReportError(MediaErrorType.Unknown, $"一時停止に失敗しました: {ex.Message}", ex);
            }
        }

        public void Stop()
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:Stop",
                        "Cannot stop - MediaPlayer is null",
                        null);
                    return;
                }
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Stop",
                    "Stopping playback",
                    new { currentTime = _mediaPlayer.Time / 1000.0 });
                
                _mediaPlayer.Stop();
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Stop",
                    "Stop command executed",
                    new { isPlaying = _mediaPlayer.IsPlaying });
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:Stop",
                    "Exception in Stop",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                
                ReportError(MediaErrorType.Unknown, $"停止に失敗しました: {ex.Message}", ex);
            }
        }

        public async Task SeekAsync(double position)
        {
            try
            {
                if (_mediaPlayer == null || !_isLoaded.Value)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:SeekAsync",
                        "Cannot seek - MediaPlayer not loaded",
                        new { position = position, hasMediaPlayer = _mediaPlayer != null, isLoaded = _isLoaded.Value });
                    return;
                }

                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:SeekAsync",
                    "Seeking to position",
                    new { position = position, currentTime = _mediaPlayer.Time / 1000.0 });

                // MediaPlayer.Timeの設定はUIスレッドで実行する必要がある
                var time = (long)(position * 1000); // ミリ秒に変換
                
                if (System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    // UIスレッドで実行
                    _mediaPlayer.Time = time;
                }
                else
                {
                    // UIスレッドで実行
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _mediaPlayer.Time = time;
                    });
                }
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:SeekAsync",
                    "Seek completed",
                    new { position = position, newTime = _mediaPlayer.Time / 1000.0 });
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:SeekAsync",
                    "Exception in SeekAsync",
                    new { position = position, exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                
                ReportError(MediaErrorType.Unknown, $"シークに失敗しました: {ex.Message}", ex);
            }
        }

        public void SetPlaybackSpeed(double speed)
        {
            try
            {
                _playbackSpeed = Math.Clamp(speed, 0.25, 4.0);
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.SetRate((float)_playbackSpeed);
                }
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"再生速度の設定に失敗しました: {ex.Message}", ex);
            }
        }

        public void SetVolume(double volume)
        {
            try
            {
                _volume = Math.Clamp(volume, 0.0, 1.0);
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Volume = (int)(_volume * 100);
                }
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"音量の設定に失敗しました: {ex.Message}", ex);
            }
        }

        public async Task<BitmapSource?> GetCurrentFrameAsync()
        {
            try
            {
                if (_mediaPlayer == null || !_isLoaded.Value)
                    return null;

                return await Task.Run(() =>
                {
                    // LibVLCSharpでは、VideoViewを使用するか、カスタムレンダリングが必要
                    // ここでは簡易実装としてnullを返す
                    // 実際の実装では、VideoViewのスナップショット機能を使用
                    return (BitmapSource?)null;
                });
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"フレーム取得に失敗しました: {ex.Message}", ex);
                return null;
            }
        }

        public async Task<VideoInfo?> GetVideoInfoAsync()
        {
            try
            {
                if (_mediaPlayer == null || _mediaPlayer.Media == null || !_isLoaded.Value)
                    return null;

                return await Task.Run(async () =>
                {
                    var media = _mediaPlayer.Media;
                    if (media == null)
                        return null;

                    // Media.Parse()を実行してTracks情報を取得
                    try
                    {
                        await media.Parse(MediaParseOptions.ParseLocal | MediaParseOptions.FetchLocal, 5000);
                    }
                    catch
                    {
                        // Parseに失敗しても続行
                    }

                    int width = 0;
                    int height = 0;
                    double frameRate = 30.0; // デフォルト値
                    string? codec = null;

                    // Tracksから動画情報を取得
                    try
                    {
                        var tracks = media.Tracks;
                        if (tracks != null)
                        {
                            foreach (var track in tracks)
                            {
                                if (track.TrackType == TrackType.Video)
                                {
                                    var videoTrack = track.Data.Video;
                                    // VideoTrackは構造体なので、nullチェックではなく、Width/Heightが0でないかチェック
                                    if (videoTrack.Width > 0 && videoTrack.Height > 0)
                                    {
                                        width = (int)videoTrack.Width;
                                        height = (int)videoTrack.Height;
                                        
                                        // フレームレートを取得（fps）
                                        // VideoTrack.FrameRateNumとFrameRateDenから計算
                                        if (videoTrack.FrameRateDen > 0)
                                        {
                                            frameRate = (double)videoTrack.FrameRateNum / videoTrack.FrameRateDen;
                                        }
                                        
                                        // Codec情報は現時点では取得不可（MediaTrackにCodecNameプロパティがない）
                                        codec = null;
                                        
                                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                            "VideoEngine.cs:GetVideoInfoAsync",
                                            "Video track found",
                                            new { width, height, frameRate, frameRateNum = videoTrack.FrameRateNum, frameRateDen = videoTrack.FrameRateDen });
                                        
                                        break; // 最初のビデオトラックを使用
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception trackEx)
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "VideoEngine.cs:GetVideoInfoAsync",
                            "Exception reading tracks",
                            new { exceptionType = trackEx.GetType().Name, message = trackEx.Message });
                        // エラーが発生してもデフォルト値で続行
                    }

                    return new VideoInfo
                    {
                        Width = width,
                        Height = height,
                        FrameRate = frameRate,
                        Duration = _totalDuration.Value,
                        Codec = codec,
                        Bitrate = null // 現時点では取得不可
                    };
                });
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"動画情報の取得に失敗しました: {ex.Message}", ex);
                return null;
            }
        }

        private void SetupMediaPlayerEvents()
        {
            if (_mediaPlayer == null)
                return;

            _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            _mediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
            _mediaPlayer.Playing += MediaPlayer_Playing;
            _mediaPlayer.Paused += MediaPlayer_Paused;
            _mediaPlayer.Stopped += MediaPlayer_Stopped;
            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
        }

        private void PositionUpdateTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_mediaPlayer != null && _isLoaded.Value)
                {
                    var time = _mediaPlayer.Time / 1000.0; // ミリ秒から秒に変換
                    _currentPosition.OnNext(time);
                }
            }
            catch
            {
                // エラーは無視（タイマーイベントなので）
            }
        }

        private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            var time = e.Time / 1000.0; // ミリ秒から秒に変換
            _currentPosition.OnNext(time);
        }

        private void MediaPlayer_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        {
            var duration = e.Length / 1000.0; // ミリ秒から秒に変換
            _totalDuration.OnNext(duration);
            
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "VideoEngine.cs:MediaPlayer_LengthChanged",
                "LengthChanged event fired",
                new { length = e.Length, duration = duration });
        }

        private void MediaPlayer_Playing(object? sender, EventArgs e)
        {
            _isPlaying.OnNext(true);
        }

        private void MediaPlayer_Paused(object? sender, EventArgs e)
        {
            _isPlaying.OnNext(false);
        }

        private void MediaPlayer_Stopped(object? sender, EventArgs e)
        {
            _isPlaying.OnNext(false);
            _currentPosition.OnNext(0.0);
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            _isPlaying.OnNext(false);
        }

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            ReportError(MediaErrorType.DecoderError, "動画の再生中にエラーが発生しました。");
        }

        private void ReportError(MediaErrorType errorType, string message, Exception? innerException = null)
        {
            var error = new MediaError
            {
                Id = Guid.NewGuid().ToString(),
                ErrorType = errorType,
                Message = message,
                InnerException = innerException?.ToString(),
                StackTrace = innerException?.StackTrace,
                Severity = MediaErrorSeverity.Error,
                OccurredAt = DateTime.UtcNow
            };

            _errors.OnNext(error);
        }

        private bool _isDisposed = false;
        private readonly object _disposeLock = new object();

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
            }

            try
            {
                // タイマーを停止
                if (_positionUpdateTimer != null)
                {
                    _positionUpdateTimer.Stop();
                    _positionUpdateTimer.Tick -= PositionUpdateTimer_Tick;
                }

                // MediaPlayerのイベントハンドラを解除（Disposeはしない）
                if (_mediaPlayer != null)
                {
                    try
                    {
                        // イベントハンドラを解除
                        _mediaPlayer.TimeChanged -= MediaPlayer_TimeChanged;
                        _mediaPlayer.LengthChanged -= MediaPlayer_LengthChanged;
                        _mediaPlayer.Playing -= MediaPlayer_Playing;
                        _mediaPlayer.Paused -= MediaPlayer_Paused;
                        _mediaPlayer.Stopped -= MediaPlayer_Stopped;
                        _mediaPlayer.EndReached -= MediaPlayer_EndReached;
                        _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;

                        // 再生中の場合は停止
                        try
                        {
                            if (_mediaPlayer.IsPlaying)
                            {
                                _mediaPlayer.Stop();
                            }
                        }
                        catch { }

                        // MediaPlayerとLibVLCのDisposeはスキップ
                        // LibVLCSharpのDispose時にAccessViolationExceptionが発生する既知の問題があり、
                        // .NET Core/.NET 5+ではAccessViolationExceptionを捕捉できないため、
                        // ネイティブリソースの解放はOSに任せる
                        //
                        // 参考: https://github.com/videolan/libvlcsharp/issues/crash-on-dispose
                        // アプリケーション終了時にOSがプロセスのリソースを解放するため、
                        // メモリリークの心配はない
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLog(
                            "VideoEngine:Dispose",
                            "Error during MediaPlayer cleanup",
                            new { error = ex.Message });
                    }
                }

                LogHelper.WriteLog(
                    "VideoEngine:Dispose",
                    "LibVLC/MediaPlayer dispose skipped to avoid AccessViolationException",
                    null);

                // マネージドリソース（Subject）を解放
                try
                {
                    _disposables?.Dispose();
                }
                catch (Exception) { }

                try
                {
                    _currentPosition?.Dispose();
                    _totalDuration?.Dispose();
                    _isPlaying?.Dispose();
                    _isLoaded?.Dispose();
                    _errors?.Dispose();
                }
                catch (Exception) { }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "VideoEngine:Dispose",
                    "Unexpected error during dispose",
                    new { error = ex.Message });
            }
        }
    }
}
