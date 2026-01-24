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

                // 非同期で読み込み
                await Task.Run(async () =>
                {
                    if (_mediaPlayer == null)
                    {
                        _mediaPlayer = new MediaPlayer(_libVLC);
                        SetupMediaPlayerEvents();
                    }

                    var media = new Media(_libVLC, filePath, FromType.FromPath);
                    _mediaPlayer.Media = media;
                    
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:LoadAsync",
                        "Media set, before Parse",
                        new { filePath = filePath, mediaDuration = media.Duration, playerLength = _mediaPlayer.Length });
                    
                    // MediaをパースしてDurationを取得
                    // Parse()は非同期メソッドなので、awaitする必要がある
                    var parseStatus = await media.Parse(MediaParseOptions.ParseLocal | MediaParseOptions.FetchLocal, 5000);
                    
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "VideoEngine.cs:LoadAsync",
                        "Media Parse completed",
                        new { filePath = filePath, parseStatus = parseStatus.ToString(), mediaDuration = media.Duration, playerLength = _mediaPlayer.Length });
                    
                    // Media.Parse()後は、Media.Durationから直接取得できる
                    // MediaPlayer.Lengthは再生開始まで-1のままの可能性があるため、Media.Durationを使用
                    if (media.Duration > 0)
                    {
                        var duration = media.Duration / 1000.0; // ミリ秒から秒に変換
                        _totalDuration.OnNext(duration);
                        
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "VideoEngine.cs:LoadAsync",
                            "Media parsed, TotalDuration updated from Media.Duration",
                            new { filePath = filePath, mediaDuration = media.Duration, totalDuration = duration });
                    }
                    else
                    {
                        // Media.Durationが取得できない場合、MediaPlayer.Lengthを待つ
                        var timeout = DateTime.Now.AddSeconds(2);
                        var waitCount = 0;
                        while (_mediaPlayer.Length == -1 && DateTime.Now < timeout)
                        {
                            await System.Threading.Tasks.Task.Delay(100, cancellationToken);
                            waitCount++;
                        }
                        
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "VideoEngine.cs:LoadAsync",
                            "After waiting for MediaPlayer.Length",
                            new { filePath = filePath, length = _mediaPlayer.Length, waitCount = waitCount, timedOut = _mediaPlayer.Length == -1 });
                        
                        if (_mediaPlayer.Length > 0)
                        {
                            var duration = _mediaPlayer.Length / 1000.0; // ミリ秒から秒に変換
                            _totalDuration.OnNext(duration);
                            
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "VideoEngine.cs:LoadAsync",
                                "Media parsed, TotalDuration updated from MediaPlayer.Length",
                                new { filePath = filePath, totalDuration = duration });
                        }
                        else
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "VideoEngine.cs:LoadAsync",
                                "Duration not available after Parse (will wait for LengthChanged event)",
                                new { filePath = filePath, mediaDuration = media.Duration, playerLength = _mediaPlayer.Length });
                        }
                    }
                });

                _isLoaded.OnNext(true);
                
                // #region agent log
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "VideoEngine.cs:LoadAsync",
                    "LoadAsync completed successfully",
                    new { filePath = filePath, hasMediaPlayer = _mediaPlayer != null, hasMedia = _mediaPlayer?.Media != null, isLoaded = _isLoaded.Value });
                // #endregion
                
                return true;
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"動画の読み込みに失敗しました: {ex.Message}", ex);
                return false;
            }
        }

        public void Play()
        {
            try
            {
                _mediaPlayer?.Play();
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"再生に失敗しました: {ex.Message}", ex);
            }
        }

        public void Pause()
        {
            try
            {
                _mediaPlayer?.Pause();
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"一時停止に失敗しました: {ex.Message}", ex);
            }
        }

        public void Stop()
        {
            try
            {
                _mediaPlayer?.Stop();
            }
            catch (Exception ex)
            {
                ReportError(MediaErrorType.Unknown, $"停止に失敗しました: {ex.Message}", ex);
            }
        }

        public async Task SeekAsync(double position)
        {
            try
            {
                if (_mediaPlayer == null || !_isLoaded.Value)
                    return;

                await Task.Run(() =>
                {
                    var time = (long)(position * 1000); // ミリ秒に変換
                    _mediaPlayer.Time = time;
                });
            }
            catch (Exception ex)
            {
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

        public void Dispose()
        {
            if (_positionUpdateTimer != null)
            {
                _positionUpdateTimer.Stop();
                _positionUpdateTimer.Tick -= PositionUpdateTimer_Tick;
            }

            if (_mediaPlayer != null)
            {
                _mediaPlayer.TimeChanged -= MediaPlayer_TimeChanged;
                _mediaPlayer.LengthChanged -= MediaPlayer_LengthChanged;
                _mediaPlayer.Playing -= MediaPlayer_Playing;
                _mediaPlayer.Paused -= MediaPlayer_Paused;
                _mediaPlayer.Stopped -= MediaPlayer_Stopped;
                _mediaPlayer.EndReached -= MediaPlayer_EndReached;
                _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }

            if (_libVLC != null)
            {
                _libVLC.Dispose();
            }

            _disposables?.Dispose();
            _errors?.Dispose();
        }
    }
}
