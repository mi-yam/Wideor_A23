using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Features.Editor;
using Wideor.App.Features.Player;
using Wideor.App.Features.Timeline;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Shell
{
    /// <summary>
    /// シェル（メインウィンドウ）のViewModel。
    /// 各FeatureのViewModelを統合し、アプリケーション全体の初期化を管理します。
    /// </summary>
    public class ShellViewModel : IDisposable
    {
        private readonly IProjectContext _projectContext;
        private readonly CompositeDisposable _disposables = new();

        // --- Feature ViewModels ---

        /// <summary>
        /// Player機能のViewModel
        /// </summary>
        public PlayerViewModel PlayerViewModel { get; }

        /// <summary>
        /// Timeline機能のViewModel
        /// </summary>
        public TimelineViewModel TimelineViewModel { get; }

        /// <summary>
        /// Editor機能のViewModel
        /// </summary>
        public EditorViewModel EditorViewModel { get; }

        // --- Application State ---

        /// <summary>
        /// アプリケーションが初期化済みかどうか
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsInitialized { get; }

        private readonly ReactiveProperty<bool> _isInitialized = new ReactiveProperty<bool>(false);

        /// <summary>
        /// 初期化エラーメッセージ
        /// </summary>
        public IReadOnlyReactiveProperty<string?> InitializationError { get; }

        private readonly ReactiveProperty<string?> _initializationError = new ReactiveProperty<string?>();

        // --- Ribbon Commands ---

        /// <summary>
        /// 新規プロジェクト作成コマンド
        /// </summary>
        public ReactiveCommand NewProjectCommand { get; }

        /// <summary>
        /// プロジェクトを開くコマンド
        /// </summary>
        public ReactiveCommand OpenProjectCommand { get; }

        /// <summary>
        /// プロジェクトを保存するコマンド
        /// </summary>
        public ReactiveCommand SaveProjectCommand { get; }

        /// <summary>
        /// 名前を付けて保存コマンド
        /// </summary>
        public ReactiveCommand SaveAsProjectCommand { get; }

        /// <summary>
        /// 動画書き出しコマンド
        /// </summary>
            public ReactiveCommand LoadVideoCommand { get; }
            public ReactiveCommand ExportVideoCommand { get; }

        /// <summary>
        /// 元に戻すコマンド
        /// </summary>
        public ReactiveCommand UndoCommand { get; }

        /// <summary>
        /// やり直しコマンド
        /// </summary>
        public ReactiveCommand RedoCommand { get; }

        /// <summary>
        /// 切り取りコマンド
        /// </summary>
        public ReactiveCommand CutCommand { get; }

        /// <summary>
        /// コピーコマンド
        /// </summary>
        public ReactiveCommand CopyCommand { get; }

        /// <summary>
        /// 貼り付けコマンド
        /// </summary>
        public ReactiveCommand PasteCommand { get; }

        /// <summary>
        /// シアターモード切り替えコマンド
        /// </summary>
        public ReactiveCommand ToggleTheaterModeCommand { get; }

        /// <summary>
        /// プレビューモード切り替えコマンド
        /// </summary>
        public ReactiveCommand TogglePreviewModeCommand { get; }

        /// <summary>
        /// ズームインコマンド
        /// </summary>
        public ReactiveCommand ZoomInCommand { get; }

        /// <summary>
        /// ズームアウトコマンド
        /// </summary>
        public ReactiveCommand ZoomOutCommand { get; }

        /// <summary>
        /// ズームリセットコマンド
        /// </summary>
        public ReactiveCommand ZoomResetCommand { get; }

        /// <summary>
        /// バージョン情報表示コマンド
        /// </summary>
        public ReactiveCommand ShowVersionInfoCommand { get; }

        public ShellViewModel(
            IProjectContext projectContext,
            IScrollCoordinator scrollCoordinator,
            ITimeRulerService timeRulerService,
            IThumbnailProvider thumbnailProvider,
            IVideoEngine videoEngine)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));

            IsInitialized = _isInitialized.ToReadOnlyReactiveProperty(false)
                .AddTo(_disposables);

            InitializationError = _initializationError.ToReadOnlyReactiveProperty(null)
                .AddTo(_disposables);

            // 各FeatureのViewModelを初期化
            PlayerViewModel = new PlayerViewModel(videoEngine);
            TimelineViewModel = new TimelineViewModel(scrollCoordinator, timeRulerService, thumbnailProvider);
            EditorViewModel = new EditorViewModel(projectContext, scrollCoordinator);
            
            // TimelineViewModelとEditorViewModelを連携
            TimelineViewModel.EditorViewModel = EditorViewModel;

            // リボンコマンドの初期化
            NewProjectCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // #region agent log
                    try
                    {
                        System.IO.File.AppendAllText(
                            @".cursor\debug.log",
                            $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ShellViewModel.cs:159\",\"message\":\"NewProjectCommand executed\",\"data\":{{\"command\":\"NewProject\"}}}}\n");
                    }
                    catch { }
                    // #endregion
                    
                    // 未保存の変更がある場合は確認
                    if (_projectContext.IsDirty.Value && _projectContext.IsProjectLoaded.Value)
                    {
                        var result = System.Windows.MessageBox.Show(
                            "未保存の変更があります。新しいプロジェクトを作成しますか？\n（変更は失われます）",
                            "確認",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                        
                        if (result != System.Windows.MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }
                    
                    _projectContext.CreateNewProject();
                    
                    System.Windows.MessageBox.Show(
                        "新しいプロジェクトを作成しました。",
                        "新規プロジェクト",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                })
                .AddTo(_disposables);

            OpenProjectCommand = new ReactiveCommand()
                .WithSubscribe(async () =>
                {
                    // #region agent log
                    try
                    {
                        System.IO.File.AppendAllText(
                            @".cursor\debug.log",
                            $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ShellViewModel.cs:166\",\"message\":\"OpenProjectCommand executed\",\"data\":{{\"command\":\"OpenProject\"}}}}\n");
                    }
                    catch { }
                    // #endregion
                    var dialog = new OpenFileDialog
                    {
                        Filter = "Wideorプロジェクト (*.wideor)|*.wideor|すべてのファイル (*.*)|*.*",
                        Title = "プロジェクトを開く"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        var success = await _projectContext.LoadProjectAsync(dialog.FileName);
                        if (!success)
                        {
                            System.Windows.MessageBox.Show(
                                "プロジェクトの読み込みに失敗しました。\nエラー一覧を確認してください。",
                                "エラー",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);
                        }
                    }
                })
                .AddTo(_disposables);

            SaveProjectCommand = IsInitialized
                .CombineLatest(_projectContext.IsProjectLoaded, (init, loaded) => init && loaded)
                .ToReactiveCommand()
                .WithSubscribe(async () =>
                {
                    // プロジェクトファイルパスがない場合は「名前を付けて保存」を促す
                    if (string.IsNullOrWhiteSpace(_projectContext.ProjectFilePath.Value))
                    {
                        var dialog = new SaveFileDialog
                        {
                            Filter = "Wideorプロジェクト (*.wideor)|*.wideor|すべてのファイル (*.*)|*.*",
                            Title = "名前を付けて保存",
                            DefaultExt = "wideor"
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            var success = await _projectContext.SaveProjectAsync(dialog.FileName);
                            if (!success)
                            {
                                System.Windows.MessageBox.Show(
                                    "プロジェクトの保存に失敗しました。\nエラー一覧を確認してください。",
                                    "エラー",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Error);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show(
                                    "プロジェクトを保存しました。",
                                    "保存完了",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Information);
                            }
                        }
                    }
                    else
                    {
                        var success = await _projectContext.SaveProjectAsync();
                        if (!success)
                        {
                            System.Windows.MessageBox.Show(
                                "プロジェクトの保存に失敗しました。\nエラー一覧を確認してください。",
                                "エラー",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(
                                "プロジェクトを保存しました。",
                                "保存完了",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information);
                        }
                    }
                })
                .AddTo(_disposables);

            SaveAsProjectCommand = new ReactiveCommand()
                .WithSubscribe(async () =>
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "Wideorプロジェクト (*.wideor)|*.wideor|すべてのファイル (*.*)|*.*",
                        Title = "名前を付けて保存",
                        DefaultExt = "wideor"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        var success = await _projectContext.SaveProjectAsync(dialog.FileName);
                        if (!success)
                        {
                            System.Windows.MessageBox.Show(
                                "プロジェクトの保存に失敗しました。\nエラー一覧を確認してください。",
                                "エラー",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(
                                "プロジェクトを保存しました。",
                                "保存完了",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information);
                        }
                    }
                })
                .AddTo(_disposables);

            LoadVideoCommand = IsInitialized.ToReactiveCommand()
                .WithSubscribe(() =>
                {
                    // #region agent log
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "ShellViewModel.cs:LoadVideoCommand",
                        "LoadVideoCommand button clicked",
                        new { isInitialized = IsInitialized.Value, hasPlayerViewModel = PlayerViewModel != null });
                    // #endregion

                    var dialog = new OpenFileDialog
                    {
                        Filter = "動画ファイル (*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.flv)|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.flv|すべてのファイル (*.*)|*.*",
                        Title = "動画ファイルを開く"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        // #region agent log
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "ShellViewModel.cs:LoadVideoCommand",
                            "File selected",
                            new { filePath = dialog.FileName, hasPlayerViewModel = PlayerViewModel != null, hasLoadVideoCommand = PlayerViewModel?.LoadVideoCommand != null });
                        // #endregion

                        if (PlayerViewModel?.LoadVideoCommand != null)
                        {
                            // #region agent log
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "ShellViewModel.cs:LoadVideoCommand",
                                "Calling PlayerViewModel.LoadVideoCommand.Execute",
                                new { filePath = dialog.FileName });
                            // #endregion

                            PlayerViewModel.LoadVideoCommand.Execute(dialog.FileName);

                            // 動画が読み込まれたら、TimelineViewModelに動画ファイルパスを設定してサムネイルを生成
                            // PlayerViewModelのIsLoadedとTotalDurationを監視して、動画が読み込まれ、TotalDurationが更新されたときにTimelineViewModelを更新
                            var videoFilePath = dialog.FileName;
                            
                            PlayerViewModel.IsLoaded
                                .Where(loaded => loaded)
                                .Take(1) // 最初の一回だけ実行
                                .Subscribe(_ =>
                                {
                                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                        "ShellViewModel.cs:LoadVideoCommand",
                                        "Video loaded, waiting for TotalDuration",
                                        new { videoFilePath = videoFilePath });
                                    
                                    // TotalDurationが0より大きくなるまで待つ
                                    var totalDurationSubscription =                                     PlayerViewModel.TotalDuration
                                        .Where(duration => duration > 0)
                                        .Take(1) // 最初の一回だけ実行
                                        .Subscribe(async totalDuration =>
                                        {
                                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                                "ShellViewModel.cs:LoadVideoCommand",
                                                "TotalDuration updated, updating TimelineViewModel",
                                                new { videoFilePath = videoFilePath, totalDuration = totalDuration });

                                            TimelineViewModel.VideoFilePath.Value = videoFilePath;
                                            TimelineViewModel.TotalDuration.Value = totalDuration;

                                            // 動画情報を取得してfpsを設定
                                            try
                                            {
                                                var videoInfo = await PlayerViewModel.GetVideoInfoAsync();
                                                if (videoInfo != null)
                                                {
                                                    TimelineViewModel.VideoFrameRate.Value = videoInfo.FrameRate;
                                                    
                                                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                                        "ShellViewModel.cs:LoadVideoCommand",
                                                        "VideoInfo obtained",
                                                        new { videoFilePath = videoFilePath, frameRate = videoInfo.FrameRate, width = videoInfo.Width, height = videoInfo.Height });
                                                }
                                                else
                                                {
                                                    // VideoInfoが取得できない場合はデフォルト値を使用
                                                    TimelineViewModel.VideoFrameRate.Value = 30.0;
                                                    
                                                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                                        "ShellViewModel.cs:LoadVideoCommand",
                                                        "VideoInfo not available, using default fps",
                                                        new { videoFilePath = videoFilePath, defaultFps = 30.0 });
                                                }
                                            }
                                            catch (Exception videoInfoEx)
                                            {
                                                // VideoInfo取得に失敗した場合はデフォルト値を使用
                                                TimelineViewModel.VideoFrameRate.Value = 30.0;
                                                
                                                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                                    "ShellViewModel.cs:LoadVideoCommand",
                                                    "Exception getting VideoInfo, using default fps",
                                                    new { videoFilePath = videoFilePath, exceptionType = videoInfoEx.GetType().Name, message = videoInfoEx.Message, defaultFps = 30.0 });
                                            }

                                            // サムネイルを生成
                                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                                "ShellViewModel.cs:LoadVideoCommand",
                                                "Calling GenerateThumbnailsCommand",
                                                new { videoFilePath = videoFilePath, totalDuration = totalDuration, videoFps = TimelineViewModel.VideoFrameRate.Value, displayFps = TimelineViewModel.DisplayFrameRate.Value });
                                            
                                            TimelineViewModel.GenerateThumbnailsCommand.Execute();
                                        });
                                    
                                    // タイムアウト用のログ（10秒後にTotalDurationが0のままの場合）
                                    System.Threading.Tasks.Task.Delay(10000).ContinueWith(_ =>
                                    {
                                        var currentTotalDuration = PlayerViewModel.TotalDuration.Value;
                                        if (currentTotalDuration <= 0)
                                        {
                                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                                "ShellViewModel.cs:LoadVideoCommand",
                                                "TotalDuration timeout - still 0",
                                                new { videoFilePath = videoFilePath, currentTotalDuration = currentTotalDuration });
                                        }
                                    });
                                    
                                    totalDurationSubscription.AddTo(_disposables);
                                })
                                .AddTo(_disposables);
                        }
                        else
                        {
                            // #region agent log
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "ShellViewModel.cs:LoadVideoCommand",
                                "PlayerViewModel or LoadVideoCommand is null",
                                new { hasPlayerViewModel = PlayerViewModel != null, hasLoadVideoCommand = PlayerViewModel?.LoadVideoCommand != null });
                            // #endregion
                        }
                    }
                    else
                    {
                        // #region agent log
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "ShellViewModel.cs:LoadVideoCommand",
                            "File dialog cancelled",
                            null);
                        // #endregion
                    }
                })
                .AddTo(_disposables);

            ExportVideoCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: 動画書き出し処理
                })
                .AddTo(_disposables);

            UndoCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: 元に戻す処理（EditorViewModel経由など）
                })
                .AddTo(_disposables);

            RedoCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: やり直し処理（EditorViewModel経由など）
                })
                .AddTo(_disposables);

            CutCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: 切り取り処理
                })
                .AddTo(_disposables);

            CopyCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: コピー処理
                })
                .AddTo(_disposables);

            PasteCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: 貼り付け処理
                })
                .AddTo(_disposables);

            ToggleTheaterModeCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: シアターモードの切り替え（PlayerViewの表示/非表示など）
                })
                .AddTo(_disposables);

            TogglePreviewModeCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: プレビューモードの切り替え
                })
                .AddTo(_disposables);

            ZoomInCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TimelineViewModelのズームインを呼び出す
                    TimelineViewModel.ZoomInCommand.Execute();
                })
                .AddTo(_disposables);

            ZoomOutCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TimelineViewModelのズームアウトを呼び出す
                    TimelineViewModel.ZoomOutCommand.Execute();
                })
                .AddTo(_disposables);

            ZoomResetCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TimelineViewModelのズームリセットを呼び出す
                    TimelineViewModel.ZoomResetCommand.Execute();
                })
                .AddTo(_disposables);

            ShowVersionInfoCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // TODO: バージョン情報ダイアログを表示
                    System.Windows.MessageBox.Show(
                        "Wideor\nVersion 1.0.0",
                        "バージョン情報",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                })
                .AddTo(_disposables);

            // 初期化処理を開始
            _ = InitializeAsync();
        }

        /// <summary>
        /// アプリケーションの初期化処理
        /// </summary>
        private async Task InitializeAsync()
        {
            try
            {
                _initializationError.Value = null;

                // プロジェクトの初期化（必要に応じて）
                // 例: デフォルトプロジェクトの作成
                if (!_projectContext.IsProjectLoaded.Value)
                {
                    _projectContext.CreateNewProject();
                }

                // 初期化完了
                _isInitialized.Value = true;
            }
            catch (Exception ex)
            {
                _initializationError.Value = $"初期化エラー: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"ShellViewModel初期化エラー: {ex}");
            }
        }

        public void Dispose()
        {
            PlayerViewModel?.Dispose();
            TimelineViewModel?.Dispose();
            EditorViewModel?.Dispose();
            _disposables?.Dispose();
        }
    }
}
