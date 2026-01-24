using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
    /// スクロール同期、ズーム管理、サムネイル生成を統合します。
    /// </summary>
    public class TimelineViewModel : IDisposable
    {
        private readonly IScrollCoordinator _scrollCoordinator;
        private readonly ITimeRulerService _timeRulerService;
        private readonly IThumbnailProvider _thumbnailProvider;
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
        /// サムネイルアイテムのコレクション
        /// </summary>
        public ReadOnlyObservableCollection<ThumbnailItem> ThumbnailItems { get; }

        private readonly ObservableCollection<ThumbnailItem> _thumbnailItems = new();

        /// <summary>
        /// 動画ファイルのパス（サムネイル生成用）
        /// </summary>
        public ReactiveProperty<string?> VideoFilePath { get; }

        /// <summary>
        /// 動画の総時間（秒）
        /// </summary>
        public ReactiveProperty<double> TotalDuration { get; }

        /// <summary>
        /// サムネイルの幅（ピクセル）
        /// </summary>
        public ReactiveProperty<int> ThumbnailWidth { get; }

        /// <summary>
        /// サムネイルの高さ（ピクセル）
        /// </summary>
        public ReactiveProperty<int> ThumbnailHeight { get; }

        /// <summary>
        /// 動画のフレームレート（fps）
        /// </summary>
        public ReactiveProperty<double> VideoFrameRate { get; }

        /// <summary>
        /// 表示フレームレート（fps）- タイムラインに表示するフレームレート
        /// </summary>
        public ReactiveProperty<double> DisplayFrameRate { get; }

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

        /// <summary>
        /// サムネイルを生成するコマンド
        /// </summary>
        public ReactiveCommand GenerateThumbnailsCommand { get; }

        private CancellationTokenSource? _thumbnailCancellation;
        private Task<Dictionary<double, System.Windows.Media.Imaging.BitmapSource>>? _thumbnailTask;
        private bool _isGeneratingThumbnails = false; // サムネイル生成中のフラグ

        /// <summary>
        /// EditorViewModelへの参照（SceneBlocksを取得するため）
        /// </summary>
        public EditorViewModel? EditorViewModel { get; set; }

        public TimelineViewModel(
            IScrollCoordinator scrollCoordinator,
            ITimeRulerService timeRulerService,
            IThumbnailProvider thumbnailProvider)
        {
            _scrollCoordinator = scrollCoordinator ?? throw new ArgumentNullException(nameof(scrollCoordinator));
            _timeRulerService = timeRulerService ?? throw new ArgumentNullException(nameof(timeRulerService));
            _thumbnailProvider = thumbnailProvider ?? throw new ArgumentNullException(nameof(thumbnailProvider));

            // ITimeRulerServiceのPixelsPerSecondを購読
            PixelsPerSecond = _timeRulerService.PixelsPerSecond
                .ToReadOnlyReactiveProperty(100.0) // デフォルト: 100px/秒
                .AddTo(_disposables);

            // IScrollCoordinatorのスクロール位置を購読
            ScrollPosition = _scrollCoordinator.ScrollPosition
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);

            // サムネイルアイテムのコレクション
            // コレクション同期を有効化（非UIスレッドからの更新を許可）
            BindingOperations.EnableCollectionSynchronization(_thumbnailItems, new object());
            ThumbnailItems = new ReadOnlyObservableCollection<ThumbnailItem>(_thumbnailItems);

            // プロパティの初期化
            VideoFilePath = new ReactiveProperty<string?>()
                .AddTo(_disposables);

            TotalDuration = new ReactiveProperty<double>(0.0)
                .AddTo(_disposables);

            ThumbnailWidth = new ReactiveProperty<int>(160)
                .AddTo(_disposables);

            ThumbnailHeight = new ReactiveProperty<int>(90)
                .AddTo(_disposables);

            VideoFrameRate = new ReactiveProperty<double>(30.0) // デフォルト: 30fps
                .AddTo(_disposables);

            DisplayFrameRate = new ReactiveProperty<double>(1.0) // デフォルト: 1fps（1秒に1フレーム表示）
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

            GenerateThumbnailsCommand = VideoFilePath
                .Select(path => !string.IsNullOrEmpty(path))
                .ToReactiveCommand()
                .WithSubscribe(() =>
                {
                    // Taskの例外をキャッチしてログに記録
                    _ = GenerateThumbnailsAsync().ContinueWith(task =>
                    {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsCommand",
                                "Task faulted",
                                new { exceptionType = task.Exception.InnerException?.GetType().Name, message = task.Exception.InnerException?.Message, stackTrace = task.Exception.InnerException?.StackTrace });
                        }
                    }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                })
                .AddTo(_disposables);

            // PixelsPerSecondまたはDisplayFrameRateが変更されたら、サムネイルを再生成（最適化版）
            PixelsPerSecond
                .Skip(1) // 初期値はスキップ
                .Throttle(TimeSpan.FromMilliseconds(500)) // ズーム操作が完了してから500ms後に生成（高速化）
                .Where(_ => !_isGeneratingThumbnails) // 既に生成中の場合はスキップ
                .Subscribe(async _ =>
                {
                    await RegenerateThumbnailsIfNeeded();
                })
                .AddTo(_disposables);

            DisplayFrameRate
                .Skip(1) // 初期値はスキップ
                .Throttle(TimeSpan.FromMilliseconds(500)) // 表示fps変更が完了してから500ms後に生成
                .Where(_ => !_isGeneratingThumbnails) // 既に生成中の場合はスキップ
                .Subscribe(async _ =>
                {
                    await RegenerateThumbnailsIfNeeded();
                })
                .AddTo(_disposables);
        }

        /// <summary>
        /// サムネイルを再生成する必要がある場合に再生成します
        /// </summary>
        private async Task RegenerateThumbnailsIfNeeded()
        {
            // 既に生成中の場合はスキップ
            if (_isGeneratingThumbnails)
            {
                return;
            }

            if (string.IsNullOrEmpty(VideoFilePath.Value) || TotalDuration.Value <= 0)
            {
                return;
            }

            try
            {
                await GenerateThumbnailsAsync();
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合は何もしない
            }
            catch (Exception ex)
            {
                // エラーをログに記録（UIをクラッシュさせないため）
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:RegenerateThumbnailsIfNeeded",
                    "Exception in RegenerateThumbnailsIfNeeded",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
            }
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

        /// <summary>
        /// 表示範囲内のThumbnailItemのIsActiveを更新（最適化版）
        /// </summary>
        public void UpdateVisibleThumbnailStates(double viewportTop, double viewportBottom, IReadOnlyList<SceneBlock> sceneBlocks)
        {
            try
            {
                if (sceneBlocks == null)
                    return;

                var margin = 100; // 上下100pxのマージン
                var topWithMargin = viewportTop - margin;
                var bottomWithMargin = viewportBottom + margin;

                // SceneBlocksを時間範囲でソートして、バイナリサーチを可能にする（最適化）
                var sortedBlocks = sceneBlocks
                    .Where(block => block != null)
                    .OrderBy(block => block.StartTime)
                    .ToList();

                // UIスレッドで実行されていることを確認
                if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        UpdateVisibleThumbnailStates(viewportTop, viewportBottom, sceneBlocks));
                    return;
                }

                // 表示範囲内のアイテムのみを処理（パフォーマンス向上）
                // YPositionでソートされていることを前提に、範囲検索を最適化
                var visibleItems = _thumbnailItems
                    .Where(item => item != null && item.YPosition >= topWithMargin && item.YPosition <= bottomWithMargin)
                    .ToList();

                foreach (var item in visibleItems)
                {
                    try
                    {
                        // バイナリサーチで該当するSceneBlockを検索（最適化）
                        var isActive = sortedBlocks.Any(block =>
                            item.TimePosition >= block.StartTime && item.TimePosition <= block.EndTime);
                        
                        // 値が変更された場合のみ更新（不要な通知を避ける）
                        if (item.IsActive.Value != isActive)
                        {
                            item.IsActive.Value = isActive;
                        }
                    }
                    catch (Exception itemEx)
                    {
                        // 個別のアイテム更新エラーは無視して続行
                        System.Diagnostics.Debug.WriteLine($"Error updating item state: {itemEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // エラーをログに記録（クラッシュを防ぐ）
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:UpdateVisibleThumbnailStates",
                    "Error updating visible thumbnail states",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        /// <summary>
        /// サムネイルを生成します。
        /// </summary>
        private async Task GenerateThumbnailsAsync()
        {
            // 既に生成中の場合はスキップ
            if (_isGeneratingThumbnails)
            {
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Skipping thumbnail generation (already in progress)",
                        new { videoFilePath = VideoFilePath.Value, totalDuration = TotalDuration.Value });
                }
                catch { }
                // #endregion
                return;
            }
            
            // #region agent log
            try
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:GenerateThumbnailsAsync",
                    "GenerateThumbnailsAsync called",
                    new { videoFilePath = VideoFilePath.Value, totalDuration = TotalDuration.Value });
            }
            catch { }
            // #endregion
            
            if (string.IsNullOrEmpty(VideoFilePath.Value) || TotalDuration.Value <= 0)
            {
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Skipping thumbnail generation (invalid parameters)",
                        new { videoFilePath = VideoFilePath.Value, totalDuration = TotalDuration.Value });
                }
                catch { }
                // #endregion
                // UIスレッドでコレクションをクリアする必要がある
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => _thumbnailItems.Clear());
                return;
            }
            
            // 前回の生成をキャンセル（_thumbnailTaskをローカル変数に保存して、nullにならないようにする）
            Task<Dictionary<double, System.Windows.Media.Imaging.BitmapSource>>? previousTask = _thumbnailTask;
            CancellationTokenSource? previousCancellation = _thumbnailCancellation;
            
            // 前のタスクがある場合は、確実にキャンセルして完了を待つ
            if (previousCancellation != null || previousTask != null)
            {
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Cancelling previous thumbnail generation",
                        new { 
                            hasCancellationSource = previousCancellation != null, 
                            isCancellationRequested = previousCancellation.Token.IsCancellationRequested, 
                            hasTask = previousTask != null, 
                            taskStatus = previousTask?.Status.ToString(),
                            isTaskCompleted = previousTask?.IsCompleted ?? false
                        });
                }
                catch { }
                // #endregion
                
                try
                {
                    previousCancellation.Cancel();
                    
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "Cancellation requested",
                            new { isCancellationRequested = previousCancellation.Token.IsCancellationRequested });
                    }
                    catch { }
                    // #endregion
                }
                catch (Exception cancelEx)
                {
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "Exception cancelling previous task",
                            new { exceptionType = cancelEx.GetType().Name, message = cancelEx.Message });
                    }
                    catch { }
                    // #endregion
                }
                
                // 前回のタスクが完了するまで待つ（セマフォが解放されるのを待つ）
                if (previousTask != null)
                {
                    try
                    {
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                "Waiting for previous task to complete",
                                new { 
                                    taskStatus = previousTask.Status.ToString(), 
                                    isFaulted = previousTask.IsFaulted, 
                                    isCanceled = previousTask.IsCanceled,
                                    isCompleted = previousTask.IsCompleted
                                });
                        }
                        catch { }
                        // #endregion
                        
                        // タイムアウト付きで待機（最大3秒に延長）
                        var waitTask = Task.WhenAny(previousTask, Task.Delay(3000));
                        
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                "Task.WhenAny called",
                                null);
                        }
                        catch { }
                        // #endregion
                        
                        await waitTask;
                        
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                "Previous task wait completed",
                                new { 
                                    taskStatus = previousTask.Status.ToString(), 
                                    isCompleted = previousTask.IsCompleted, 
                                    isFaulted = previousTask.IsFaulted, 
                                    isCanceled = previousTask.IsCanceled 
                                });
                        }
                        catch { }
                        // #endregion
                        
                        // タスクが完了していない場合、例外を確認
                        if (previousTask.IsFaulted && previousTask.Exception != null)
                        {
                            // #region agent log
                            try
                            {
                                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                    "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                    "Previous task faulted",
                                    new { 
                                        exceptionType = previousTask.Exception.InnerException?.GetType().Name, 
                                        message = previousTask.Exception.InnerException?.Message, 
                                        stackTrace = previousTask.Exception.InnerException?.StackTrace 
                                    });
                            }
                            catch { }
                            // #endregion
                        }
                        
                        // タスクが完了していない場合は、最大3秒まで待つ
                        if (!previousTask.IsCompleted)
                        {
                            // #region agent log
                            try
                            {
                                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                    "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                    "Previous task not completed, waiting with timeout",
                                    null);
                            }
                            catch { }
                            // #endregion
                            
                            try
                            {
                                previousTask.Wait(TimeSpan.FromSeconds(3));
                            }
                            catch (Exception waitEx)
                            {
                                // #region agent log
                                try
                                {
                                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                        "Exception waiting for previous task completion",
                                        new { exceptionType = waitEx.GetType().Name, message = waitEx.Message });
                                }
                                catch { }
                                // #endregion
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                "Exception waiting for previous task",
                                new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });
                        }
                        catch { }
                        // #endregion
                    }
                }
                else
                {
                    // タスクがない場合、キャンセルが反映されるまで少し待つ
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "No previous task, waiting for cancellation to propagate",
                            null);
                    }
                    catch { }
                    // #endregion
                    await Task.Delay(200); // 100msから200msに延長
                }
                
                // 前回のCancellationTokenSourceを破棄（タスクの待機後）
                try
                {
                    previousCancellation.Dispose();
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "Previous CancellationTokenSource disposed",
                            null);
                    }
                    catch { }
                    // #endregion
                }
                catch (Exception disposeEx)
                {
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "Exception disposing CancellationTokenSource",
                            new { exceptionType = disposeEx.GetType().Name, message = disposeEx.Message });
                    }
                    catch {                     }
                    // #endregion
                }
                
                // 前のタスクが完了するまで、最大5秒まで待つ（セマフォが解放されるのを確実に待つ）
                if (previousTask != null && !previousTask.IsCompleted)
                {
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "Waiting for previous task to fully complete",
                            new { taskStatus = previousTask.Status.ToString(), isCompleted = previousTask.IsCompleted });
                    }
                    catch { }
                    // #endregion
                    
                    try
                    {
                        // 前のタスクが完了するまで最大5秒待つ
                        await Task.WhenAny(previousTask, Task.Delay(5000));
                        
                        // まだ完了していない場合は、強制的に待つ
                        if (!previousTask.IsCompleted)
                        {
                            previousTask.Wait(TimeSpan.FromSeconds(2));
                        }
                        
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                "Previous task fully completed",
                                new { taskStatus = previousTask.Status.ToString(), isCompleted = previousTask.IsCompleted });
                        }
                        catch { }
                        // #endregion
                    }
                    catch (Exception waitEx)
                    {
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync",
                                "Exception waiting for previous task",
                                new { exceptionType = waitEx.GetType().Name, message = waitEx.Message });
                        }
                        catch { }
                        // #endregion
                    }
                }
                
                // 前のタスクが完了したことを確認するため、少し待つ
                await Task.Delay(100);
            }
            
            // 生成フラグを設定（前のタスクが完了した後）
            _isGeneratingThumbnails = true;
            
            // #region agent log
            try
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:GenerateThumbnailsAsync",
                    "Creating new CancellationTokenSource",
                    null);
            }
            catch { }
            // #endregion
            
            _thumbnailCancellation = new CancellationTokenSource();
            var cancellationToken = _thumbnailCancellation.Token;
            
            // #region agent log
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "TimelineViewModel.cs:GenerateThumbnailsAsync",
                "New cancellation token created",
                new { isCancellationRequested = cancellationToken.IsCancellationRequested });
            // #endregion

            try
            {
                var totalDuration = TotalDuration.Value;
                var pixelsPerSecond = PixelsPerSecond.Value;

                // ズームレベルに基づいてフレーム間隔を計算
                // 基準：PixelsPerSecond = 100 → 1秒間隔
                // ズームイン：PixelsPerSecond = 200 → 0.5秒間隔
                // ズームアウト：PixelsPerSecond = 50 → 2秒間隔
                const double basePixelsPerSecond = 100.0; // 基準値（1秒間隔に対応）
                var timeIntervalSeconds = basePixelsPerSecond / pixelsPerSecond;
                
                var timePositions = new List<double>();

                if (totalDuration > 0 && timeIntervalSeconds > 0)
                {
                    // 過剰生成を避けるため上限を設ける（最大500フレーム）
                    var expectedCount = (int)Math.Ceiling(totalDuration / timeIntervalSeconds);
                    const int maxThumbnails = 500;
                    
                    if (expectedCount > maxThumbnails)
                    {
                        // 間隔を拡大して最大500フレームに制限
                        var stride = (int)Math.Ceiling((double)expectedCount / maxThumbnails);
                        timeIntervalSeconds *= stride;
                    }
                    
                    // 時間位置を計算（ズームベース）
                    for (double t = 0; t <= totalDuration; t += timeIntervalSeconds)
                    {
                        timePositions.Add(t);
                    }
                    
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "Calculating thumbnail sampling (zoom-based)",
                            new
                            {
                                pixelsPerSecond,
                                basePixelsPerSecond,
                                timeIntervalSeconds,
                                totalDuration,
                                expectedCount,
                                timePositionsCount = timePositions.Count
                            });
                    }
                    catch { }
                    // #endregion
                }

                // 連続体のフレームを一定間隔でサンプリング
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:GenerateThumbnailsAsync",
                    "Calling GenerateThumbnailsAsync (time positions)",
                    new { videoFilePath = VideoFilePath.Value, timePositionsCount = timePositions.Count, width = ThumbnailWidth.Value, height = ThumbnailHeight.Value });
                
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:GenerateThumbnailsAsync",
                    "Before GenerateThumbnailsAsync (time positions)",
                    new { videoFilePath = VideoFilePath.Value, timePositionsCount = timePositions.Count, width = ThumbnailWidth.Value, height = ThumbnailHeight.Value, isCancellationRequested = cancellationToken.IsCancellationRequested });
                
                // 新しいタスクを作成して追跡
                // TotalDurationを渡すことで、GetVideoDurationAsyncの呼び出しをスキップし、クラッシュのリスクを回避
                _thumbnailTask = _thumbnailProvider.GenerateThumbnailsAsync(
                    VideoFilePath.Value,
                    timePositions.ToArray(),
                    ThumbnailWidth.Value,
                    ThumbnailHeight.Value,
                    cancellationToken,
                    TotalDuration.Value > 0 ? TotalDuration.Value : (double?)null);
                
                Dictionary<double, System.Windows.Media.Imaging.BitmapSource> thumbnails = await _thumbnailTask;

                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "TimelineViewModel.cs:GenerateThumbnailsAsync",
                    "GenerateThumbnailsAsync (time positions) completed",
                    new { thumbnailCount = thumbnails.Count, hasThumbnails = thumbnails.Count > 0 });

                if (cancellationToken.IsCancellationRequested)
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Cancellation requested",
                        null);
                    return;
                }

                // サムネイルアイテムを更新（UIスレッドで実行する必要がある）
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Before Dispatcher.InvokeAsync",
                        new { thumbnailsCount = thumbnails.Count, isOnUIThread = System.Windows.Application.Current.Dispatcher.CheckAccess() });
                }
                catch { }
                // #endregion
                
                try
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // #region agent log
                            try
                            {
                                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                    "TimelineViewModel.cs:GenerateThumbnailsAsync:Dispatcher.InvokeAsync",
                                    "Dispatcher.InvokeAsync lambda started",
                                    new { thumbnailsCount = thumbnails.Count, currentItemsCount = _thumbnailItems.Count });
                            }
                            catch { }
                            // #endregion
                            
                            // 既存のサムネイルを再利用するための辞書を作成（パフォーマンス最適化）
                            var existingItemsByTime = _thumbnailItems.ToDictionary(item => item.TimePosition);
                            
                            // 新しいサムネイルのTimePositionセット
                            var newTimePositions = new HashSet<double>(thumbnails.Keys);
                            
                            // 既存のアイテムで、新しいTimePositionに含まれないものを削除
                            var itemsToRemove = _thumbnailItems
                                .Where(item => !newTimePositions.Contains(item.TimePosition))
                                .ToList();
                            
                            foreach (var item in itemsToRemove)
                            {
                                _thumbnailItems.Remove(item);
                            }
                            
                            // 新しいサムネイルを追加または更新
                            var addedCount = 0;
                            var updatedCount = 0;
                            var currentPixelsPerSecond = PixelsPerSecond.Value;
                            
                            foreach (var kvp in thumbnails.OrderBy(x => x.Key))
                            {
                                try
                                {
                                    var yPosition = TimeToY(kvp.Key);
                                    
                                    if (existingItemsByTime.TryGetValue(kvp.Key, out var existingItem))
                                    {
                                        // 既存アイテムのYPositionを更新（サムネイルは再利用）
                                        existingItem.YPosition = yPosition;
                                        updatedCount++;
                                    }
                                    else
                                    {
                                        // 新しいアイテムを追加
                                        var thumbnailItem = new ThumbnailItem
                                        {
                                            TimePosition = kvp.Key,
                                            Thumbnail = kvp.Value,
                                            YPosition = yPosition
                                        };
                                        _thumbnailItems.Add(thumbnailItem);
                                        addedCount++;
                                    }
                                }
                                catch (Exception addEx)
                                {
                                    // #region agent log
                                    try
                                    {
                                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                            "TimelineViewModel.cs:GenerateThumbnailsAsync:Dispatcher.InvokeAsync",
                                            "Exception adding/updating thumbnail item",
                                            new { timePosition = kvp.Key, exceptionType = addEx.GetType().Name, message = addEx.Message, stackTrace = addEx.StackTrace });
                                    }
                                    catch { }
                                    // #endregion
                                }
                            }
                            
                            // YPositionでソート（効率的な表示のため）
                            var sortedItems = _thumbnailItems.OrderBy(item => item.YPosition).ToList();
                            _thumbnailItems.Clear();
                            foreach (var item in sortedItems)
                            {
                                _thumbnailItems.Add(item);
                            }
                            
                            // #region agent log
                            try
                            {
                                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                    "TimelineViewModel.cs:GenerateThumbnailsAsync:Dispatcher.InvokeAsync",
                                    "ThumbnailItems updated",
                                    new { addedCount = addedCount, updatedCount = updatedCount, removedCount = itemsToRemove.Count, totalCount = _thumbnailItems.Count });
                            }
                            catch { }
                            // #endregion
                        }
                        catch (Exception lambdaEx)
                        {
                            // #region agent log
                            try
                            {
                                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                    "TimelineViewModel.cs:GenerateThumbnailsAsync:Dispatcher.InvokeAsync",
                                    "Exception in Dispatcher.InvokeAsync lambda",
                                    new { exceptionType = lambdaEx.GetType().Name, message = lambdaEx.Message, stackTrace = lambdaEx.StackTrace, innerException = lambdaEx.InnerException?.ToString() });
                            }
                            catch { }
                            // #endregion
                            throw;
                        }
                    });
                    
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "Dispatcher.InvokeAsync completed",
                            new { finalItemsCount = _thumbnailItems.Count });
                    }
                    catch { }
                    // #endregion
                }
                catch (Exception dispatcherEx)
                {
                    // #region agent log
                    try
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "TimelineViewModel.cs:GenerateThumbnailsAsync",
                            "Exception awaiting Dispatcher.InvokeAsync",
                            new { exceptionType = dispatcherEx.GetType().Name, message = dispatcherEx.Message, stackTrace = dispatcherEx.StackTrace, innerException = dispatcherEx.InnerException?.ToString() });
                    }
                    catch { }
                    // #endregion
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Operation cancelled",
                        null);
                }
                catch { }
                // #endregion
                // キャンセルされた場合は何もしない
            }
            catch (Exception ex)
            {
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Exception occurred",
                        new { errorMessage = ex.Message, stackTrace = ex.StackTrace, exceptionType = ex.GetType().Name, innerException = ex.InnerException?.ToString() });
                }
                catch { }
                // #endregion
                System.Diagnostics.Debug.WriteLine($"サムネイル生成エラー: {ex.Message}");
            }
            finally
            {
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Finally block entered",
                        new { hasTask = _thumbnailTask != null, taskStatus = _thumbnailTask?.Status.ToString() });
                }
                catch { }
                // #endregion
                
                // タスクが完了するまで待つ（リソースを確実に解放するため）
                if (_thumbnailTask != null)
                {
                    try
                    {
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync:finally",
                                "Waiting for task to complete",
                                new { taskStatus = _thumbnailTask.Status.ToString(), isCompleted = _thumbnailTask.IsCompleted });
                        }
                        catch { }
                        // #endregion
                        
                        // タスクが完了するまで最大3秒待つ
                        if (!_thumbnailTask.IsCompleted)
                        {
                            _thumbnailTask.Wait(TimeSpan.FromSeconds(3));
                        }
                        
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync:finally",
                                "Task wait completed",
                                new { taskStatus = _thumbnailTask.Status.ToString(), isCompleted = _thumbnailTask.IsCompleted, isFaulted = _thumbnailTask.IsFaulted });
                        }
                        catch { }
                        // #endregion
                    }
                    catch (Exception waitEx)
                    {
                        // #region agent log
                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "TimelineViewModel.cs:GenerateThumbnailsAsync:finally",
                                "Exception waiting for task",
                                new { exceptionType = waitEx.GetType().Name, message = waitEx.Message });
                        }
                        catch { }
                        // #endregion
                    }
                }
                
                // タスクをリセットする前に、確実に完了したことを確認
                _thumbnailTask = null;
                
                // 生成フラグをリセット
                _isGeneratingThumbnails = false;
                
                // #region agent log
                try
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "TimelineViewModel.cs:GenerateThumbnailsAsync",
                        "Finally block completed",
                        null);
                }
                catch { }
                // #endregion
            }
        }

        public void Dispose()
        {
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
            _disposables?.Dispose();
        }
    }

    /// <summary>
    /// サムネイルアイテム（Timeline機能専用）
    /// </summary>
    public class ThumbnailItem
    {
        public double TimePosition { get; set; }
        public System.Windows.Media.Imaging.BitmapSource? Thumbnail { get; set; }
        public double YPosition { get; set; }
        
        /// <summary>
        /// このフレームがアクティブかどうか（コマンド行が存在する時間帯かどうか）
        /// </summary>
        public ReactiveProperty<bool> IsActive { get; } = new ReactiveProperty<bool>(true);
    }
}
