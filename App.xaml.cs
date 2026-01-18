using System;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using System.Collections.ObjectModel;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // DIコンテナのセットアップ
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // ShellWindowを表示
            var shellWindow = _serviceProvider.GetRequiredService<ShellWindow>();
            var shellViewModel = _serviceProvider.GetRequiredService<ShellViewModel>();
            shellWindow.ViewModel = shellViewModel;
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

            // IThumbnailProvider
            services.AddSingleton<IThumbnailProvider, StubThumbnailProvider>();

            // IVideoEngine
            services.AddSingleton<IVideoEngine, StubVideoEngine>();

            // --- Feature ViewModels ---
            // 注: ViewModelは通常、Viewごとに新しいインスタンスが必要なためTransientまたはScoped
            // ただし、ShellViewModelが所有するため、ここでは登録しない

            // --- Shell ---
            services.AddTransient<ShellViewModel>();
            services.AddTransient<ShellWindow>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
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
    }

    internal class StubScrollCoordinator : IScrollCoordinator
    {
        private readonly Reactive.Bindings.ReactiveProperty<double> _scrollPosition = new(0.0);
        private readonly Reactive.Bindings.ReactiveProperty<double> _maxScrollOffset = new(0.0);
        private readonly Reactive.Bindings.ReactiveProperty<bool> _isScrollEnabled = new(true);

        public Reactive.Bindings.IReadOnlyReactiveProperty<double> ScrollPosition => _scrollPosition;
        public Reactive.Bindings.IReadOnlyReactiveProperty<double> MaxScrollOffset => _maxScrollOffset;
        public Reactive.Bindings.IReadOnlyReactiveProperty<bool> IsScrollEnabled => _isScrollEnabled;

        public void SetScrollPosition(double position) => _scrollPosition.Value = Math.Clamp(position, 0.0, 1.0);
        public void SetScrollOffset(double offset) => _scrollPosition.Value = Math.Clamp(offset / Math.Max(1, _maxScrollOffset.Value), 0.0, 1.0);
        public System.IDisposable RegisterScrollViewer(System.Windows.Controls.ScrollViewer scrollViewer) => new System.Reactive.Disposables.CompositeDisposable();
        public System.IDisposable SubscribeScrollChanged(Action<double> onScrollChanged) => System.Reactive.Disposables.Disposable.Empty;
        public void SetScrollEnabled(bool enabled) => _isScrollEnabled.Value = enabled;
        public void SetMaxScrollOffset(double maxOffset) => _maxScrollOffset.Value = maxOffset;
        public void Reset() => _scrollPosition.Value = 0.0;
    }

    internal class StubTimeRulerService : ITimeRulerService
    {
        private readonly Reactive.Bindings.ReactiveProperty<double> _pixelsPerSecond = new(100.0);

        public Reactive.Bindings.IReadOnlyReactiveProperty<double> PixelsPerSecond => _pixelsPerSecond;

        public void SetZoomLevel(double pixelsPerSecond) => _pixelsPerSecond.Value = Math.Max(10, pixelsPerSecond);

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
            string videoFilePath, double[] timePositions, int width = 160, int height = 90, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.Dictionary<double, System.Windows.Media.Imaging.BitmapSource>());

        public System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<double, System.Windows.Media.Imaging.BitmapSource>> GenerateThumbnailsEvenlyAsync(
            string videoFilePath, int count, int width = 160, int height = 90, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.Dictionary<double, System.Windows.Media.Imaging.BitmapSource>());

        public System.Threading.Tasks.Task<System.Windows.Media.Imaging.BitmapSource?> GenerateThumbnailFromImageAsync(
            string imageFilePath, int width = 160, int height = 90, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);
    }

    internal class StubVideoEngine : IVideoEngine
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<double> _currentPosition = new(0.0);
        private readonly System.Reactive.Subjects.BehaviorSubject<double> _totalDuration = new(0.0);
        private readonly System.Reactive.Subjects.BehaviorSubject<bool> _isPlaying = new(false);
        private readonly System.Reactive.Subjects.BehaviorSubject<bool> _isLoaded = new(false);
        private readonly System.Reactive.Subjects.Subject<Wideor.App.Shared.Domain.MediaError> _errors = new();

        public System.IObservable<double> CurrentPosition => _currentPosition;
        public System.IObservable<double> TotalDuration => _totalDuration;
        public System.IObservable<bool> IsPlaying => _isPlaying;
        public System.IObservable<bool> IsLoaded => _isLoaded;
        public System.IObservable<Wideor.App.Shared.Domain.MediaError> Errors => _errors;

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
}
