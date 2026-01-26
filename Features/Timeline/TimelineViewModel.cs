using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Features.Editor;
using Wideor.App.Shared.Domain;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// タイムライン機能のViewModel。
    /// スクロール同期、ズーム管理、動画セグメント管理を統合します。
    /// </summary>
    public class TimelineViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IScrollCoordinator _scrollCoordinator;
        private readonly ITimeRulerService _timeRulerService;
        private readonly IVideoSegmentManager _segmentManager;
        private readonly ICommandExecutor _commandExecutor;
        private readonly ICommandParser _commandParser;
        private readonly IVideoEngine _videoEngine;
        private readonly CompositeDisposable _disposables = new();

        // --- Reactive Properties ---

        /// <summary>
        /// 現在のズームレベル（1秒あたりのピクセル数）
        /// </summary>
        public IReadOnlyReactiveProperty<double> PixelsPerSecond { get; }

        /// <summary>
        /// 現在のスクロール位置（0.0～1.0）
        /// </summary>
        public IReadOnlyReactiveProperty<double> ScrollPosition { get; }

        /// <summary>
        /// ScrollCoordinator（ViewからScrollViewerを登録するために公開）
        /// </summary>
        public IScrollCoordinator ScrollCoordinator => _scrollCoordinator;

        /// <summary>
        /// タイムラインの総高さ（ピクセル）
        /// </summary>
        public IReadOnlyReactiveProperty<double> TotalHeight { get; }

        /// <summary>
        /// 動画ファイルのパス
        /// </summary>
        public ReactiveProperty<string?> VideoFilePath { get; }

        /// <summary>
        /// 動画の総時間（秒）
        /// </summary>
        public ReactiveProperty<double> TotalDuration { get; }

        /// <summary>
        /// ビデオセグメントのコレクション
        /// </summary>
        public ReadOnlyObservableCollection<VideoSegment> VideoSegments { get; }

        private readonly ObservableCollection<VideoSegment> _videoSegments = new();

        /// <summary>
        /// 現在再生中のセグメント
        /// </summary>
        public ReactiveProperty<VideoSegment?> CurrentPlayingSegment { get; }

        /// <summary>
        /// MediaPlayer（VideoSegmentViewで使用）
        /// </summary>
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer
        {
            get => _mediaPlayer;
            private set
            {
                if (_mediaPlayer != value)
                {
                    _mediaPlayer = value;
                    OnPropertyChanged(nameof(MediaPlayer));
                }
            }
        }
        
        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;

        /// <summary>
        /// 動画の読み込み状態
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsLoaded { get; }

        /// <summary>
        /// 現在の再生位置（秒）
        /// </summary>
        public IReadOnlyReactiveProperty<double> CurrentPosition { get; }

        // --- Commands ---

        /// <summary>
        /// ズームインコマンド
        /// </summary>
        public ReactiveCommand ZoomInCommand { get; }

        /// <summary>
        /// ズームアウトコマンド
        /// </summary>
        public ReactiveCommand ZoomOutCommand { get; }

        /// <summary>
        /// ズームリセットコマンド（デフォルトズームレベルに戻す）
        /// </summary>
        public ReactiveCommand ZoomResetCommand { get; }

        public TimelineViewModel(
            IScrollCoordinator scrollCoordinator,
            ITimeRulerService timeRulerService,
            IVideoSegmentManager segmentManager,
            ICommandExecutor commandExecutor,
            ICommandParser commandParser,
            IVideoEngine videoEngine)
        {
            _scrollCoordinator = scrollCoordinator ?? throw new ArgumentNullException(nameof(scrollCoordinator));
            _timeRulerService = timeRulerService ?? throw new ArgumentNullException(nameof(timeRulerService));
            _segmentManager = segmentManager ?? throw new ArgumentNullException(nameof(segmentManager));
            _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
            _commandParser = commandParser ?? throw new ArgumentNullException(nameof(commandParser));
            _videoEngine = videoEngine ?? throw new ArgumentNullException(nameof(videoEngine));

            // 固定スケール: 1秒 = 100ピクセル（ズーム機能なし）
            PixelsPerSecond = new ReactiveProperty<double>(100.0)
                .ToReadOnlyReactiveProperty()
                .AddTo(_disposables);

            // IScrollCoordinatorのスクロール位置を購読
            ScrollPosition = _scrollCoordinator.ScrollPosition
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);

            // ビデオセグメントのコレクション
            // コレクション同期を有効化（非UIスレッドからの更新を許可）
            BindingOperations.EnableCollectionSynchronization(_videoSegments, new object());
            VideoSegments = new ReadOnlyObservableCollection<VideoSegment>(_videoSegments);

            // 現在再生中のセグメント
            CurrentPlayingSegment = new ReactiveProperty<VideoSegment?>()
                .AddTo(_disposables);

            // 動画の読み込み状態
            IsLoaded = _videoEngine.IsLoaded
                .ToReadOnlyReactiveProperty(false)
                .AddTo(_disposables);

            // 初期MediaPlayerを設定
            MediaPlayer = _videoEngine.MediaPlayer;

            // MediaPlayerの変更を監視（IsLoadedがtrueになったときにMediaPlayerを更新）
            IsLoaded
                .Where(loaded => loaded)
                .Subscribe(_ =>
                {
                    MediaPlayer = _videoEngine.MediaPlayer;
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel:IsLoaded",
                        "MediaPlayer updated",
                        new { hasMediaPlayer = MediaPlayer != null });
                })
                .AddTo(_disposables);

            // 現在の再生位置
            CurrentPosition = _videoEngine.CurrentPosition
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);
            
            // 再生位置を監視して、セグメントの終了時間に達したら停止
            CurrentPosition
                .CombineLatest(CurrentPlayingSegment, (position, segment) => new { Position = position, Segment = segment })
                .Where(x => x.Segment != null && x.Position >= x.Segment.EndTime)
                .Subscribe(x =>
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:CurrentPosition",
                        "Segment end reached, stopping playback",
                        new { segmentId = x.Segment.Id, position = x.Position, endTime = x.Segment.EndTime });
                    
                    _videoEngine.Stop();
                    x.Segment.State = SegmentState.Stopped;
                    CurrentPlayingSegment.Value = null;
                })
                .AddTo(_disposables);

            // プロパティの初期化
            VideoFilePath = new ReactiveProperty<string?>()
                .AddTo(_disposables);

            TotalDuration = new ReactiveProperty<double>(0.0)
                .AddTo(_disposables);

            // 総高さの計算（TotalDuration × PixelsPerSecond）
            TotalHeight = TotalDuration
                .CombineLatest(PixelsPerSecond, (duration, pps) => duration * pps)
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);

            // スクロール位置が変更されたら、MaxScrollOffsetを更新
            TotalHeight
                .Subscribe(height => _scrollCoordinator.SetMaxScrollOffset(Math.Max(0, height - 100))) // 100pxはマージン
                .AddTo(_disposables);

            // コマンドの初期化
            ZoomInCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:ZoomInCommand",
                            "ZoomInCommand executed",
                            new { currentPixelsPerSecond = PixelsPerSecond.Value });
                    }
                    catch { }
                    // #endregion
                    
                    var current = PixelsPerSecond.Value;
                    var newZoom = current * 1.5;
                    
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:ZoomInCommand",
                            "Calling SetZoomLevel",
                            new { currentZoom = current, newZoom = newZoom });
                    }
                    catch { }
                    // #endregion
                    
                    _timeRulerService.SetZoomLevel(newZoom);
                    
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:ZoomInCommand",
                            "SetZoomLevel completed",
                            new { pixelsPerSecond = PixelsPerSecond.Value });
                    }
                    catch { }
                    // #endregion
                })
                .AddTo(_disposables);

            ZoomOutCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:ZoomOutCommand",
                            "ZoomOutCommand executed",
                            new { currentPixelsPerSecond = PixelsPerSecond.Value });
                    }
                    catch { }
                    // #endregion
                    
                    var current = PixelsPerSecond.Value;
                    var newZoom = current / 1.5;
                    
                    // ズームレベルの範囲を制限（10～1000 px/s）
                    newZoom = Math.Max(10, Math.Min(1000, newZoom));
                    
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:ZoomOutCommand",
                            "Calling SetZoomLevel",
                            new { currentZoom = current, newZoom = newZoom });
                    }
                    catch { }
                    // #endregion
                    
                    _timeRulerService.SetZoomLevel(newZoom);
                    
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:ZoomOutCommand",
                            "SetZoomLevel completed",
                            new { pixelsPerSecond = PixelsPerSecond.Value });
                    }
                    catch { }
                    // #endregion
                })
                .AddTo(_disposables);

            ZoomResetCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:ZoomResetCommand",
                            "ZoomResetCommand executed",
                            new { currentPixelsPerSecond = PixelsPerSecond.Value });
                    }
                    catch { }
                    // #endregion
                    
                    // デフォルトズームレベル（100 px/s）にリセット
                    _timeRulerService.SetZoomLevel(100.0);
                    
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:ZoomResetCommand",
                            "Zoom reset completed",
                            new { pixelsPerSecond = PixelsPerSecond.Value });
                    }
                    catch { }
                    // #endregion
                })
                .AddTo(_disposables);

            // セグメント変更イベントの購読
            _segmentManager.SegmentAdded += OnSegmentAdded;
            _segmentManager.SegmentRemoved += OnSegmentRemoved;
            _segmentManager.SegmentUpdated += OnSegmentUpdated;

            // 既存のセグメントを初期化
            foreach (var segment in _segmentManager.Segments)
            {
                _videoSegments.Add(segment);
            }
        }

        /// <summary>
        /// セグメント追加イベントハンドラ
        /// </summary>
        private void OnSegmentAdded(object? sender, VideoSegmentEventArgs e)
        {
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "TimelineViewModel.cs:OnSegmentAdded",
                "Segment added event received",
                new { segmentId = e.Segment.Id, startTime = e.Segment.StartTime, endTime = e.Segment.EndTime, currentCount = _videoSegments.Count });
            
            // UIスレッドで同期的に実行（InvokeAsyncではなくInvokeを使用）
            if (System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                // 既にUIスレッドにいる場合は直接実行
                _videoSegments.Add(e.Segment);
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:OnSegmentAdded",
                    "Segment added to collection (UI thread)",
                    new { segmentId = e.Segment.Id, newCount = _videoSegments.Count });
            }
            else
            {
                // UIスレッドでない場合は同期的に実行
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _videoSegments.Add(e.Segment);
                    
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:OnSegmentAdded",
                        "Segment added to collection (dispatched)",
                        new { segmentId = e.Segment.Id, newCount = _videoSegments.Count });
                });
            }
        }

        /// <summary>
        /// セグメント削除イベントハンドラ
        /// </summary>
        private void OnSegmentRemoved(object? sender, VideoSegmentEventArgs e)
        {
            if (System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                var segment = _videoSegments.FirstOrDefault(s => s.Id == e.Segment.Id);
                if (segment != null)
                {
                    _videoSegments.Remove(segment);
                }
            }
            else
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var segment = _videoSegments.FirstOrDefault(s => s.Id == e.Segment.Id);
                    if (segment != null)
                    {
                        _videoSegments.Remove(segment);
                    }
                });
            }
        }

        /// <summary>
        /// セグメント更新イベントハンドラ
        /// </summary>
        private void OnSegmentUpdated(object? sender, VideoSegmentEventArgs e)
        {
            if (System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                var index = _videoSegments.ToList().FindIndex(s => s.Id == e.Segment.Id);
                if (index >= 0)
                {
                    _videoSegments[index] = e.Segment;
                }
            }
            else
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var index = _videoSegments.ToList().FindIndex(s => s.Id == e.Segment.Id);
                    if (index >= 0)
                    {
                        _videoSegments[index] = e.Segment;
                    }
                });
            }
        }

        /// <summary>
        /// 現在ロードされている動画ファイルパス
        /// </summary>
        private string? _currentLoadedVideoPath;

        /// <summary>
        /// 動画切り替え中フラグ（デッドロック防止）
        /// </summary>
        private bool _isSwitchingVideo = false;

        /// <summary>
        /// 現在ロードされている動画ファイルパスを設定
        /// （ShellViewModelから動画ロード後に呼び出す）
        /// </summary>
        public void SetCurrentLoadedVideoPath(string? videoPath)
        {
            _currentLoadedVideoPath = videoPath;
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "TimelineViewModel.cs:SetCurrentLoadedVideoPath",
                "Current loaded video path set",
                new { videoPath = videoPath });
        }

        /// <summary>
        /// セグメントをクリックした時の処理
        /// </summary>
        public async void OnSegmentClicked(VideoSegment segment)
        {
            // 動画切り替え中は処理しない（デッドロック防止）
            if (_isSwitchingVideo)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:OnSegmentClicked",
                    "Already switching video, ignoring click",
                    new { segmentId = segment.Id });
                return;
            }

            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "TimelineViewModel.cs:OnSegmentClicked",
                "Segment clicked",
                new { segmentId = segment.Id, startTime = segment.StartTime, endTime = segment.EndTime, videoFilePath = segment.VideoFilePath });
            
            // 非表示セグメントは再生しない
            if (!segment.Visible || segment.State == SegmentState.Hidden)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:OnSegmentClicked",
                    "Segment is hidden, skipping playback",
                    new { segmentId = segment.Id, visible = segment.Visible, state = segment.State.ToString() });
                return;
            }
            
            // 現在再生中のセグメントを確認
            var currentSegment = CurrentPlayingSegment.Value;
            if (currentSegment != null && currentSegment.Id == segment.Id)
            {
                // 同じセグメントがクリックされた場合は、再生/一時停止を切り替え
                if (currentSegment.State == SegmentState.Playing)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:OnSegmentClicked",
                        "Pausing current segment",
                        new { segmentId = segment.Id });
                    _videoEngine.Pause();
                    currentSegment.State = SegmentState.Stopped;
                    CurrentPlayingSegment.Value = null;
                    return;
                }
                else if (_videoEngine.CurrentIsLoaded)
                {
                    // 停止中で動画がロード済みなら再生を再開
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:OnSegmentClicked",
                        "Resuming segment playback",
                        new { segmentId = segment.Id });
                    currentSegment.State = SegmentState.Playing;
                    CurrentPlayingSegment.Value = currentSegment;
                    _videoEngine.Play();
                    return;
                }
            }

            try
            {
                _isSwitchingVideo = true;

                // 別のセグメントが再生中の場合は停止
                if (currentSegment != null && currentSegment.Id != segment.Id)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:OnSegmentClicked",
                        "Stopping current segment",
                        new { currentSegmentId = currentSegment.Id, newSegmentId = segment.Id });
                    currentSegment.State = SegmentState.Stopped;
                    _videoEngine.Stop();
                    await Task.Delay(50); // 停止が完了するまで待機
                }

                // 動画ファイルが異なる場合は再読み込み
                if (_currentLoadedVideoPath != segment.VideoFilePath)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:OnSegmentClicked",
                        "Loading different video file",
                        new { 
                            segmentId = segment.Id, 
                            currentPath = _currentLoadedVideoPath, 
                            newPath = segment.VideoFilePath 
                        });

                    // 新しい動画をロード
                    var loadResult = await _videoEngine.LoadAsync(segment.VideoFilePath);
                    if (!loadResult)
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:OnSegmentClicked",
                            "Failed to load video",
                            new { segmentId = segment.Id, videoFilePath = segment.VideoFilePath });
                        return;
                    }

                    _currentLoadedVideoPath = segment.VideoFilePath;
                    MediaPlayer = _videoEngine.MediaPlayer;

                    // MediaPlayerの更新を待機
                    await Task.Delay(100);
                }

                // 動画が読み込まれているか確認
                if (!_videoEngine.CurrentIsLoaded)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:OnSegmentClicked",
                        "Video not loaded",
                        new { segmentId = segment.Id });
                    return;
                }

                // クリックされたセグメントを再生
                segment.State = SegmentState.Playing;
                CurrentPlayingSegment.Value = segment;

                // セグメントの開始時間にシークして再生
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:OnSegmentClicked",
                    "Seeking to segment start time",
                    new { segmentId = segment.Id, startTime = segment.StartTime });
                
                await _videoEngine.SeekAsync(segment.StartTime);
                
                // シーク後に少し待ってから再生
                await Task.Delay(100);
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:OnSegmentClicked",
                    "Starting playback",
                    new { segmentId = segment.Id, startTime = segment.StartTime });
                
                _videoEngine.Play();
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:OnSegmentClicked",
                    "Playback started successfully",
                    new { segmentId = segment.Id, startTime = segment.StartTime, endTime = segment.EndTime });
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:OnSegmentClicked",
                    "Error playing segment",
                    new { segmentId = segment.Id, startTime = segment.StartTime, exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                
                segment.State = SegmentState.Stopped;
                CurrentPlayingSegment.Value = null;
            }
            finally
            {
                _isSwitchingVideo = false;
            }
        }

        /// <summary>
        /// エンターキーで分割
        /// </summary>
        public void CutAtCurrentTime(double currentTime)
        {
            var command = new EditCommand
            {
                Type = CommandType.Cut,
                Time = currentTime
            };
            _commandExecutor.ExecuteCommand(command);
        }

        /// <summary>
        /// スクロール位置を設定します。
        /// </summary>
        public void SetScrollPosition(double position)
        {
            _scrollCoordinator.SetScrollPosition(position);
        }

        /// <summary>
        /// スクロールオフセットを設定します。
        /// </summary>
        public void SetScrollOffset(double offset)
        {
            _scrollCoordinator.SetScrollOffset(offset);
        }

        /// <summary>
        /// ズームレベルを設定します。
        /// </summary>
        public void SetZoomLevel(double pixelsPerSecond)
        {
            // ズームレベルの範囲を制限（10～1000 px/s）
            var clampedZoom = Math.Max(10, Math.Min(1000, pixelsPerSecond));
            _timeRulerService.SetZoomLevel(clampedZoom);
        }

        /// <summary>
        /// 時間をY座標に変換します。
        /// </summary>
        public double TimeToY(double time)
        {
            return _timeRulerService.TimeToY(time);
        }

        /// <summary>
        /// Y座標を時間に変換します。
        /// </summary>
        public double YToTime(double y)
        {
            return _timeRulerService.YToTime(y);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            // セグメントイベントの購読を解除
            _segmentManager.SegmentAdded -= OnSegmentAdded;
            _segmentManager.SegmentRemoved -= OnSegmentRemoved;
            _segmentManager.SegmentUpdated -= OnSegmentUpdated;

            _disposables?.Dispose();
        }
    }
}
