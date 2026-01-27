using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using System.Collections.ObjectModel;
using LibVLCSharp.Shared;
using Wideor.App.Features.Editor;
using Wideor.App.Features.Player;
using Wideor.App.Features.Timeline;
using Wideor.App.Shell;
using Wideor.App.Shared.Domain;
using Wideor.App.Shared.Infra;

namespace Wideor_A23
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// Composition Root: DIコンテナのセットアップとアプリケーションの起動
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        /// <summary>
        /// DIコンテナのServiceProvider（外部からアクセス可能）
        /// </summary>
        public static ServiceProvider? ServiceProvider => (Current as App)?._serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 未処理の例外をハンドリング（ポップアップを防ぐため）
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            
            // #region agent log
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "App.xaml.cs:OnStartup",
                "Exception handlers registered",
                new { hasDispatcherHandler = true });
            // #endregion

            // LibVLCの初期化（VideoEngineの前に実行）
            LibVLCSharp.Shared.Core.Initialize();

            // FFmpegの初期化（バックグラウンドでダウンロード）
            _ = InitializeFFmpegAsync();

            // DIコンテナのセットアップ
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // ShellWindowを表示
            var shellWindow = _serviceProvider.GetRequiredService<ShellWindow>();
            var shellViewModel = _serviceProvider.GetRequiredService<ShellViewModel>();
            
            // #region agent log
            // LogHelperの静的コンストラクタを実行させるために、GetLogFilePath()を呼ぶ
            var logPath = Wideor.App.Shared.Infra.LogHelper.GetLogFilePath();
            System.Diagnostics.Debug.WriteLine($"[App.xaml.cs] LogFilePath: {logPath}");
            
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "App.xaml.cs:OnStartup",
                "Setting ViewModel",
                new { hasShellWindow = shellWindow != null, hasShellViewModel = shellViewModel != null, hasLoadVideoCommand = shellViewModel?.LoadVideoCommand != null });
            // #endregion
            
            if (shellWindow != null && shellViewModel != null)
            {
                shellWindow.ViewModel = shellViewModel;
                shellWindow.DataContext = shellViewModel; // DataContextも設定
            }
            
            // #region agent log
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "App.xaml.cs:OnStartup",
                "ViewModel set",
                new { shellWindowViewModel = shellWindow.ViewModel != null, shellWindowDataContext = shellWindow.DataContext != null });
            // #endregion
            
            shellWindow.Show();
        }

        /// <summary>
        /// DIコンテナにサービスを登録
        /// </summary>
        private void ConfigureServices(IServiceCollection services)
        {
            // --- Shared Infrastructure Services ---
            // 注: 実装クラスは後で追加する必要があります
            // ここではスタブ実装を登録（実際の実装に置き換えてください）

            // IProjectContext
            services.AddSingleton<IProjectContext, Wideor.App.Shared.Infra.ProjectContext>();

            // IScrollCoordinator
            services.AddSingleton<IScrollCoordinator, StubScrollCoordinator>();

            // ITimeRulerService
            services.AddSingleton<ITimeRulerService, StubTimeRulerService>();

            // IThumbnailCache
            services.AddSingleton<IThumbnailCache, Wideor.App.Shared.Infra.ThumbnailCache>();

            // IThumbnailProvider (ベース実装)
            services.AddSingleton<Wideor.App.Shared.Infra.ThumbnailProvider>();

            // IThumbnailProvider (高度な実装)
            services.AddSingleton<IThumbnailProvider>(sp =>
            {
                var baseProvider = sp.GetRequiredService<Wideor.App.Shared.Infra.ThumbnailProvider>();
                var cache = sp.GetRequiredService<IThumbnailCache>();
                return new Wideor.App.Shared.Infra.AdvancedThumbnailProvider(baseProvider, cache);
            });

            // IVideoEngine
            services.AddSingleton<IVideoEngine, Wideor.App.Shared.Infra.VideoEngine>();

            // ICommandParser
            services.AddSingleton<ICommandParser, Wideor.App.Shared.Infra.CommandParser>();

            // IVideoSegmentManager
            services.AddSingleton<IVideoSegmentManager, Wideor.App.Shared.Infra.VideoSegmentManager>();

            // ICommandExecutor
            services.AddSingleton<ICommandExecutor, Wideor.App.Shared.Infra.CommandExecutor>();

            // IVideoExporter（FFmpegを使用した動画書き出し）
            services.AddSingleton<IVideoExporter, Wideor.App.Shared.Infra.VideoExporter>();

            // IProjectFileService（テキスト形式の.wideorファイル処理）
            services.AddSingleton<IProjectFileService, Wideor.App.Shared.Infra.ProjectFileService>();

            // --- Feature ViewModels ---
            // 注: ViewModelは通常、Viewごとに新しいインスタンスが必要なためTransientまたはScoped
            // ただし、ShellViewModelが所有するため、ここでは登録しない

            // --- Shell ---
            // ShellViewModelを登録（IThumbnailCacheを注入）
            services.AddSingleton<ShellViewModel>(sp =>
            {
                var projectContext = sp.GetRequiredService<IProjectContext>();
                var scrollCoordinator = sp.GetRequiredService<IScrollCoordinator>();
                var timeRulerService = sp.GetRequiredService<ITimeRulerService>();
                var videoEngine = sp.GetRequiredService<IVideoEngine>();
                var segmentManager = sp.GetRequiredService<IVideoSegmentManager>();
                var commandExecutor = sp.GetRequiredService<ICommandExecutor>();
                var commandParser = sp.GetRequiredService<ICommandParser>();
                var thumbnailCache = sp.GetRequiredService<IThumbnailCache>();
                var videoExporter = sp.GetRequiredService<IVideoExporter>();
                var projectFileService = sp.GetRequiredService<IProjectFileService>();
                return new ShellViewModel(
                    projectContext, 
                    scrollCoordinator, 
                    timeRulerService, 
                    videoEngine, 
                    segmentManager, 
                    commandExecutor, 
                    commandParser, 
                    thumbnailCache,
                    videoExporter,
                    projectFileService);
            });
            services.AddTransient<ShellWindow>();
        }

        /// <summary>
        /// FFmpegの初期化（バックグラウンドでダウンロード）
        /// </summary>
        private async Task InitializeFFmpegAsync()
        {
            try
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "App.xaml.cs:InitializeFFmpegAsync",
                    "Starting FFmpeg initialization",
                    null);

                var progress = new Progress<Wideor.App.Shared.Infra.FFmpegDownloadProgress>(p =>
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "App.xaml.cs:InitializeFFmpegAsync",
                        "FFmpeg download progress",
                        new { stage = p.Stage.ToString(), message = p.Message, progress = p.Progress });
                });

                var result = await Wideor.App.Shared.Infra.FFmpegManager.InitializeAsync(progress);

                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "App.xaml.cs:InitializeFFmpegAsync",
                    "FFmpeg initialization completed",
                    new { success = result, isAvailable = Wideor.App.Shared.Infra.FFmpegManager.IsAvailable });
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "App.xaml.cs:InitializeFFmpegAsync",
                    "FFmpeg initialization failed",
                    new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // #region agent log
            try
            {
                // 完全な例外情報をログに記録
                var fullExceptionString = e.Exception.ToString();
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "App.xaml.cs:DispatcherUnhandledException",
                    "Unhandled exception on UI thread",
                    new { 
                        exceptionType = e.Exception.GetType().Name, 
                        message = e.Exception.Message, 
                        stackTrace = e.Exception.StackTrace,
                        innerException = e.Exception.InnerException?.ToString(),
                        fullException = fullExceptionString,
                        handled = e.Handled
                    });
            }
            catch (Exception logEx)
            {
                // ログ記録自体が失敗した場合でも、デバッグ出力は試みる
                System.Diagnostics.Debug.WriteLine($"Failed to log exception: {logEx.Message}");
                System.Diagnostics.Debug.WriteLine($"Original exception: {e.Exception}");
            }
            // #endregion

            // エラーをログに記録し、アプリケーションを継続
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");
            System.Diagnostics.Debug.WriteLine($"Full exception: {e.Exception.ToString()}");
            System.Diagnostics.Debug.WriteLine($"Inner exception: {e.Exception.InnerException}");
            e.Handled = true; // 例外を処理済みとしてマーク（ポップアップを防ぐ）
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // #region agent log
            if (e.ExceptionObject is Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "App.xaml.cs:CurrentDomain_UnhandledException",
                    "Unhandled exception in AppDomain",
                    new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, isTerminating = e.IsTerminating });
            }
            // #endregion

            System.Diagnostics.Debug.WriteLine($"Unhandled exception in AppDomain: {e.ExceptionObject}");
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            // #region agent log
            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                "App.xaml.cs:TaskScheduler_UnobservedTaskException",
                "Unobserved task exception",
                new { exceptionType = e.Exception.GetType().Name, innerExceptions = e.Exception.InnerExceptions?.Select(ex => new { type = ex.GetType().Name, message = ex.Message }).ToArray(), message = e.Exception.Message, stackTrace = e.Exception.StackTrace });
            // #endregion

            System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {e.Exception}");
            e.SetObserved(); // 例外を観測済みとしてマーク（アプリケーションを継続）
        }
    }

    // --- Stub Implementations (一時的な実装、後で実際の実装に置き換える) ---

    internal class StubProjectContext : IProjectContext
    {
        private readonly Reactive.Bindings.ReactiveProperty<string?> _projectFilePath = new();
        private readonly Reactive.Bindings.ReactiveProperty<bool> _isProjectLoaded = new(false);
        private readonly Reactive.Bindings.ReactiveProperty<bool> _isDirty = new(false);
        private readonly System.Collections.ObjectModel.ObservableCollection<Wideor.App.Shared.Domain.SceneBlock> _sceneBlocks = new();
        private readonly System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.SceneBlock> _readOnlySceneBlocks;
        private readonly Reactive.Bindings.ReactiveProperty<System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.SceneBlock>> _sceneBlocksProperty;
        private readonly Reactive.Bindings.ReactiveProperty<Wideor.App.Shared.Domain.SceneBlock?> _selectedSceneBlock = new();
        private readonly Reactive.Bindings.ReactiveProperty<double> _currentPlaybackPosition = new(0.0);
        private readonly Reactive.Bindings.ReactiveProperty<double> _totalDuration = new(0.0);
        private readonly System.Collections.ObjectModel.ObservableCollection<Wideor.App.Shared.Domain.MediaError> _errors = new();
        private readonly System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.MediaError> _readOnlyErrors;
        private readonly Reactive.Bindings.ReactiveProperty<System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.MediaError>> _errorsProperty;

        public StubProjectContext()
        {
            _readOnlySceneBlocks = new System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.SceneBlock>(_sceneBlocks);
            _sceneBlocksProperty = new Reactive.Bindings.ReactiveProperty<System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.SceneBlock>>(_readOnlySceneBlocks);
            _readOnlyErrors = new System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.MediaError>(_errors);
            _errorsProperty = new Reactive.Bindings.ReactiveProperty<System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.MediaError>>(_readOnlyErrors);
        }

        public Reactive.Bindings.IReadOnlyReactiveProperty<string?> ProjectFilePath => _projectFilePath;
        public Reactive.Bindings.IReadOnlyReactiveProperty<bool> IsProjectLoaded => _isProjectLoaded;
        public Reactive.Bindings.IReadOnlyReactiveProperty<bool> IsDirty => _isDirty;
        public Reactive.Bindings.IReadOnlyReactiveProperty<System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.SceneBlock>> SceneBlocks => _sceneBlocksProperty;
        public Reactive.Bindings.IReadOnlyReactiveProperty<Wideor.App.Shared.Domain.SceneBlock?> SelectedSceneBlock => _selectedSceneBlock;
        public Reactive.Bindings.IReadOnlyReactiveProperty<double> CurrentPlaybackPosition => _currentPlaybackPosition;
        public Reactive.Bindings.IReadOnlyReactiveProperty<double> TotalDuration => _totalDuration;
        public Reactive.Bindings.IReadOnlyReactiveProperty<System.Collections.ObjectModel.ReadOnlyObservableCollection<Wideor.App.Shared.Domain.MediaError>> Errors => _errorsProperty;

        public System.IObservable<ProjectContextChangedEventArgs> ProjectChanged => System.Reactive.Linq.Observable.Empty<ProjectContextChangedEventArgs>();

        public System.Threading.Tasks.Task<bool> LoadProjectAsync(string filePath, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(true);

        public System.Threading.Tasks.Task<bool> SaveProjectAsync(string? filePath = null, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(true);

        public void CreateNewProject() => _isProjectLoaded.Value = true;

        public void AddSceneBlock(Wideor.App.Shared.Domain.SceneBlock sceneBlock) => _sceneBlocks.Add(sceneBlock);

        public bool RemoveSceneBlock(string sceneBlockId)
        {
            var block = _sceneBlocks.FirstOrDefault(b => b.Id == sceneBlockId);
            if (block != null)
            {
                _sceneBlocks.Remove(block);
                return true;
            }
            return false;
        }

        public bool UpdateSceneBlock(Wideor.App.Shared.Domain.SceneBlock sceneBlock)
        {
            var index = _sceneBlocks.ToList().FindIndex(b => b.Id == sceneBlock.Id);
            if (index >= 0)
            {
                _sceneBlocks[index] = sceneBlock;
                return true;
            }
            return false;
        }

        public void SetSelectedSceneBlock(Wideor.App.Shared.Domain.SceneBlock? sceneBlock) => _selectedSceneBlock.Value = sceneBlock;

        public void SetPlaybackPosition(double position) => _currentPlaybackPosition.Value = position;

        public void AddError(Wideor.App.Shared.Domain.MediaError error) => _errors.Add(error);

        public void ClearError(string? errorId = null)
        {
            if (errorId == null)
                _errors.Clear();
            else
            {
                var error = _errors.FirstOrDefault(e => e.Id == errorId);
                if (error != null)
                    _errors.Remove(error);
            }
        }

        public void SetThumbnailData(Wideor.App.Shared.Domain.ThumbnailData? thumbnailData, string? videoFilePath = null)
        {
            // スタブ実装：何もしない
        }
    }

    internal class StubScrollCoordinator : IScrollCoordinator
    {
        private readonly Reactive.Bindings.ReactiveProperty<double> _scrollPosition = new(0.0);
        private readonly Reactive.Bindings.ReactiveProperty<double> _maxScrollOffset = new(0.0);
        private readonly Reactive.Bindings.ReactiveProperty<bool> _isScrollEnabled = new(true);
        private readonly System.Collections.Generic.List<System.Windows.Controls.ScrollViewer> _scrollViewers = new();
        private bool _isUpdatingScroll = false;

        public Reactive.Bindings.IReadOnlyReactiveProperty<double> ScrollPosition => _scrollPosition;
        public Reactive.Bindings.IReadOnlyReactiveProperty<double> MaxScrollOffset => _maxScrollOffset;
        public Reactive.Bindings.IReadOnlyReactiveProperty<bool> IsScrollEnabled => _isScrollEnabled;

        public void SetScrollPosition(double position)
        {
            var clamped = Math.Clamp(position, 0.0, 1.0);
            _scrollPosition.Value = clamped;
            UpdateAllScrollViewers(clamped);
        }

        public void SetScrollOffset(double offset)
        {
            var position = Math.Clamp(offset / Math.Max(1, _maxScrollOffset.Value), 0.0, 1.0);
            _scrollPosition.Value = position;
            UpdateAllScrollViewers(position);
        }

        public System.IDisposable RegisterScrollViewer(System.Windows.Controls.ScrollViewer scrollViewer)
        {
            if (scrollViewer == null)
                return System.Reactive.Disposables.Disposable.Empty;

            _scrollViewers.Add(scrollViewer);

            // スクロールイベントを購読
            scrollViewer.ScrollChanged += (sender, e) =>
            {
                if (!_isUpdatingScroll && _isScrollEnabled.Value)
                {
                    var position = CalculateScrollPosition(scrollViewer);
                    _scrollPosition.Value = position;
                    UpdateOtherScrollViewers(scrollViewer, position);
                }
            };

            return System.Reactive.Disposables.Disposable.Create(() => _scrollViewers.Remove(scrollViewer));
        }

        public System.IDisposable SubscribeScrollChanged(Action<double> onScrollChanged)
        {
            return _scrollPosition.Subscribe(onScrollChanged);
        }

        public void SetScrollEnabled(bool enabled) => _isScrollEnabled.Value = enabled;
        public void SetMaxScrollOffset(double maxOffset) => _maxScrollOffset.Value = maxOffset;
        public void Reset() => SetScrollPosition(0.0);

        private double CalculateScrollPosition(System.Windows.Controls.ScrollViewer scrollViewer)
        {
            if (scrollViewer.ScrollableHeight <= 0)
                return 0.0;
            return Math.Clamp(scrollViewer.VerticalOffset / scrollViewer.ScrollableHeight, 0.0, 1.0);
        }

        private void UpdateAllScrollViewers(double position)
        {
            if (!_isScrollEnabled.Value)
                return;

            _isUpdatingScroll = true;
            try
            {
                foreach (var viewer in _scrollViewers.ToList()) // ToList()でコピーを作成して、コレクション変更エラーを防ぐ
                {
                    try
                    {
                        if (viewer != null && viewer.ScrollableHeight > 0)
                        {
                            var offset = position * viewer.ScrollableHeight;
                            viewer.ScrollToVerticalOffset(offset);
                        }
                    }
                    catch (Exception ex)
                    {
                        // #region agent log
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "App.xaml.cs:UpdateAllScrollViewers",
                            "Failed to update scroll viewer",
                            new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, position, scrollableHeight = viewer?.ScrollableHeight ?? 0 });
                        // #endregion
                    }
                }
            }
            finally
            {
                _isUpdatingScroll = false;
            }
        }

        private void UpdateOtherScrollViewers(System.Windows.Controls.ScrollViewer source, double position)
        {
            if (!_isScrollEnabled.Value)
                return;

            _isUpdatingScroll = true;
            try
            {
                foreach (var viewer in _scrollViewers.ToList()) // ToList()でコピーを作成して、コレクション変更エラーを防ぐ
                {
                    try
                    {
                        if (viewer != null && viewer != source && viewer.ScrollableHeight > 0)
                        {
                            var offset = position * viewer.ScrollableHeight;
                            viewer.ScrollToVerticalOffset(offset);
                        }
                    }
                    catch (Exception ex)
                    {
                        // #region agent log
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "App.xaml.cs:UpdateOtherScrollViewers",
                            "Failed to update scroll viewer",
                            new { exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace, position, scrollableHeight = viewer?.ScrollableHeight ?? 0 });
                        // #endregion
                    }
                }
            }
            finally
            {
                _isUpdatingScroll = false;
            }
        }
    }

    internal class StubTimeRulerService : ITimeRulerService
    {
        private readonly Reactive.Bindings.ReactiveProperty<double> _pixelsPerSecond = new(100.0);

        public Reactive.Bindings.IReadOnlyReactiveProperty<double> PixelsPerSecond => _pixelsPerSecond;

        public void SetZoomLevel(double pixelsPerSecond) => _pixelsPerSecond.Value = Math.Max(10, Math.Min(1000, pixelsPerSecond));

        public double TimeToY(double time) => time * _pixelsPerSecond.Value;

        public double YToTime(double y) => y / _pixelsPerSecond.Value;
    }

    internal class StubThumbnailProvider : IThumbnailProvider
    {
        public System.IObservable<ThumbnailGenerationProgress> Progress => System.Reactive.Linq.Observable.Empty<ThumbnailGenerationProgress>();
        public System.IObservable<Wideor.App.Shared.Domain.MediaError> Errors => System.Reactive.Linq.Observable.Empty<Wideor.App.Shared.Domain.MediaError>();

        public System.Threading.Tasks.Task<System.Windows.Media.Imaging.BitmapSource?> GenerateThumbnailAsync(
            string videoFilePath, double timePosition, int width = 160, int height = 90, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);

        public System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<double, System.Windows.Media.Imaging.BitmapSource>> GenerateThumbnailsAsync(
            string videoFilePath, double[] timePositions, int width = 160, int height = 90, System.Threading.CancellationToken cancellationToken = default, double? knownDuration = null) =>
            System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.Dictionary<double, System.Windows.Media.Imaging.BitmapSource>());

        public System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<double, System.Windows.Media.Imaging.BitmapSource>> GenerateThumbnailsEvenlyAsync(
            string videoFilePath, int count, int width = 160, int height = 90, System.Threading.CancellationToken cancellationToken = default, double? knownDuration = null) =>
            System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.Dictionary<double, System.Windows.Media.Imaging.BitmapSource>());

        public System.Threading.Tasks.Task<System.Windows.Media.Imaging.BitmapSource?> GenerateThumbnailFromImageAsync(
            string imageFilePath, int width = 160, int height = 90, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);

        public void Dispose()
        {
            // スタブ実装：何もしない
        }
    }

    internal class StubVideoEngine : IVideoEngine
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<double> _currentPosition = new(0.0);
        private readonly System.Reactive.Subjects.BehaviorSubject<double> _totalDuration = new(0.0);
        private readonly System.Reactive.Subjects.BehaviorSubject<bool> _isPlaying = new(false);
        private readonly System.Reactive.Subjects.BehaviorSubject<bool> _isLoaded = new(false);
        private readonly System.Reactive.Subjects.Subject<Wideor.App.Shared.Domain.MediaError> _errors = new();

        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => null;

        public System.IObservable<double> CurrentPosition => _currentPosition;
        public System.IObservable<double> TotalDuration => _totalDuration;
        public System.IObservable<bool> IsPlaying => _isPlaying;
        public System.IObservable<bool> IsLoaded => _isLoaded;
        public System.IObservable<Wideor.App.Shared.Domain.MediaError> Errors => _errors;
        
        /// <summary>
        /// 動画の総時間の現在値（秒）
        /// </summary>
        public double CurrentTotalDuration => _totalDuration.Value;
        
        /// <summary>
        /// 動画の読み込み状態の現在値
        /// </summary>
        public bool CurrentIsLoaded => _isLoaded.Value;

        public System.Threading.Tasks.Task<bool> LoadAsync(string filePath, System.Threading.CancellationToken cancellationToken = default)
        {
            _isLoaded.OnNext(true);
            _totalDuration.OnNext(100.0);
            return System.Threading.Tasks.Task.FromResult(true);
        }

        public void Play() => _isPlaying.OnNext(true);
        public void Pause() => _isPlaying.OnNext(false);
        public void Stop() => _isPlaying.OnNext(false);
        public System.Threading.Tasks.Task SeekAsync(double position) { _currentPosition.OnNext(position); return System.Threading.Tasks.Task.CompletedTask; }
        public void SetPlaybackSpeed(double speed) { }
        public void SetVolume(double volume) { }
        public System.Threading.Tasks.Task<System.Windows.Media.Imaging.BitmapSource?> GetCurrentFrameAsync() => System.Threading.Tasks.Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);
        public System.Threading.Tasks.Task<VideoInfo?> GetVideoInfoAsync() => System.Threading.Tasks.Task.FromResult<VideoInfo?>(null);

        public void Dispose()
        {
            _currentPosition.Dispose();
            _totalDuration.Dispose();
            _isPlaying.Dispose();
            _isLoaded.Dispose();
            _errors.Dispose();
        }
    }

    internal class StubCommandExecutor : ICommandExecutor
    {
        public void ExecuteCommand(EditCommand command) { /* スタブ: 何もしない */ }
        public void ExecuteCommands(IEnumerable<EditCommand> commands) { /* スタブ: 何もしない */ }
        public CommandResult ExecuteCommandWithResult(EditCommand command) => CommandResult.Ok(command);
        public CommandExecutionReport ExecuteCommandsWithReport(IEnumerable<EditCommand> commands) => new CommandExecutionReport();
        public Task<CommandResult> ExecuteLoadAsync(EditCommand command) => Task.FromResult(CommandResult.Ok(command));
    }
}
