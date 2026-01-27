using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Features.Editor;
using Wideor.App.Features.Player;
using Wideor.App.Features.Timeline;
using Wideor.App.Shared.Domain;
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
        private readonly IThumbnailCache? _thumbnailCache;
        private readonly IVideoEngine _videoEngine;
        private readonly IVideoSegmentManager _segmentManager;
        private readonly ICommandExecutor _commandExecutor;
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

        // --- 編集コマンド（テキストエリアに挿入） ---

        /// <summary>
        /// 現在の再生位置で分割（CUTコマンド）
        /// </summary>
        public ReactiveCommand SplitAtCurrentPositionCommand { get; }

        /// <summary>
        /// 選択セグメントを非表示（HIDEコマンド）
        /// </summary>
        public ReactiveCommand HideSelectedSegmentCommand { get; }

        /// <summary>
        /// 選択セグメントを表示（SHOWコマンド）
        /// </summary>
        public ReactiveCommand ShowSelectedSegmentCommand { get; }

        /// <summary>
        /// 選択セグメントを削除（DELETEコマンド）
        /// </summary>
        public ReactiveCommand DeleteSelectedSegmentCommand { get; }

        /// <summary>
        /// セパレータを挿入（パラグラフ対応）
        /// </summary>
        public ReactiveCommand InsertSeparatorCommand { get; }

        /// <summary>
        /// 現在の再生位置でセパレータを挿入
        /// </summary>
        public ReactiveCommand InsertSeparatorAtCurrentPositionCommand { get; }

        public ShellViewModel(
            IProjectContext projectContext,
            IScrollCoordinator scrollCoordinator,
            ITimeRulerService timeRulerService,
            IVideoEngine videoEngine,
            IVideoSegmentManager segmentManager,
            ICommandExecutor commandExecutor,
            ICommandParser commandParser,
            IThumbnailCache? thumbnailCache = null)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
            _thumbnailCache = thumbnailCache;
            _videoEngine = videoEngine ?? throw new ArgumentNullException(nameof(videoEngine));
            _segmentManager = segmentManager ?? throw new ArgumentNullException(nameof(segmentManager));
            _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));

            IsInitialized = _isInitialized.ToReadOnlyReactiveProperty(false)
                .AddTo(_disposables);

            InitializationError = _initializationError.ToReadOnlyReactiveProperty(null)
                .AddTo(_disposables);

            // 各FeatureのViewModelを初期化
            PlayerViewModel = new PlayerViewModel(videoEngine);
            TimelineViewModel = new TimelineViewModel(scrollCoordinator, timeRulerService, segmentManager, commandExecutor, commandParser, videoEngine);
            EditorViewModel = new EditorViewModel(
                projectContext, 
                scrollCoordinator,
                headerParser: null,  // デフォルト実装を使用
                sceneParser: null,   // デフォルト実装を使用
                commandParser: commandParser,
                commandExecutor: commandExecutor,
                segmentManager: segmentManager);

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
                .WithSubscribe(async () =>
                {
                    Wideor.App.Shared.Infra.LogHelper.WriteLog(
                        "ShellViewModel.cs:LoadVideoCommand",
                        "LoadVideoCommand button clicked",
                        new { isInitialized = IsInitialized.Value, hasPlayerViewModel = PlayerViewModel != null, hasCommandExecutor = _commandExecutor != null, hasSegmentManager = _segmentManager != null });

                    var dialog = new OpenFileDialog
                    {
                        Filter = "動画ファイル (*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.flv)|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.flv|すべてのファイル (*.*)|*.*",
                        Title = "動画ファイルを開く"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "ShellViewModel.cs:LoadVideoCommand",
                            "File selected",
                            new { filePath = dialog.FileName });

                        var videoFilePath = dialog.FileName;

                        // VideoFilePathを設定
                        TimelineViewModel.VideoFilePath.Value = videoFilePath;

                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "ShellViewModel.cs:LoadVideoCommand",
                            "Before loading video",
                            new { videoFilePath = videoFilePath, hasCommandExecutor = _commandExecutor != null });

                        try
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "ShellViewModel.cs:LoadVideoCommand",
                                "Starting video load",
                                new { videoFilePath = videoFilePath });

                            // UIスレッドで直接LoadAsyncを呼び出す（VideoEngine内部でDispatcher.InvokeAsyncを使用しているため）
                            // Task.Runでラップするとデッドロックが発生する可能性がある
                            var loadSuccess = await _videoEngine.LoadAsync(videoFilePath, System.Threading.CancellationToken.None).ConfigureAwait(false);

                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "ShellViewModel.cs:LoadVideoCommand",
                                "LoadAsync completed",
                                new { videoFilePath = videoFilePath, success = loadSuccess, isLoaded = PlayerViewModel.IsLoaded?.Value });

                            if (!loadSuccess)
                            {
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    System.Windows.MessageBox.Show(
                                        "動画の読み込みに失敗しました。",
                                        "エラー",
                                        System.Windows.MessageBoxButton.OK,
                                        System.Windows.MessageBoxImage.Error);
                                });
                                return;
                            }

                            // TotalDurationが取得できるまで待つ（最大10秒、ポーリング間隔200ms）
                            var timeout = DateTime.Now.AddSeconds(10);
                            var checkCount = 0;
                            double totalDuration = 0;
                            while (totalDuration <= 0 && DateTime.Now < timeout)
                            {
                                totalDuration = PlayerViewModel.TotalDuration.Value;
                                if (totalDuration <= 0)
                                {
                                    await Task.Delay(200).ConfigureAwait(false);
                                    checkCount++;

                                    // 2秒ごとにログを出力
                                    if (checkCount % 10 == 0)
                                    {
                                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                            "ShellViewModel.cs:LoadVideoCommand",
                                            "Waiting for TotalDuration",
                                            new { videoFilePath = videoFilePath, checkCount = checkCount, elapsedSeconds = checkCount * 0.2 });
                                    }
                                }
                            }

                            if (totalDuration <= 0)
                            {
                                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                    "ShellViewModel.cs:LoadVideoCommand",
                                    "TotalDuration not available",
                                    new { videoFilePath = videoFilePath, checkCount = checkCount });
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    System.Windows.MessageBox.Show(
                                        "動画の長さを取得できませんでした。",
                                        "エラー",
                                        System.Windows.MessageBoxButton.OK,
                                        System.Windows.MessageBoxImage.Warning);
                                });
                                return;
                            }

                            // UIスレッドでTimelineViewModelを更新
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                TimelineViewModel.TotalDuration.Value = totalDuration;
                                TimelineViewModel.SetCurrentLoadedVideoPath(videoFilePath);
                            });

                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "ShellViewModel.cs:LoadVideoCommand",
                                "TotalDuration obtained, adding LOAD command to editor",
                                new { videoFilePath = videoFilePath, totalDuration = totalDuration });

                            // テキストファースト: テキストエリアにLOADコマンドを追加するだけ
                            // コマンドの実行はEditorViewModelのProcessTextChangeで行われる
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                // EditorのテキストにLOADコマンドを自動追加
                                AddLoadCommandToEditor(videoFilePath);
                            });

                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "ShellViewModel.cs:LoadVideoCommand",
                                "LOAD command added to editor (text-first)",
                                new { videoFilePath = videoFilePath, duration = totalDuration });
                        }
                        catch (Exception ex)
                        {
                            Wideor.App.Shared.Infra.LogHelper.WriteLog(
                                "ShellViewModel.cs:LoadVideoCommand",
                                "Exception loading video or executing LOAD command",
                                new { videoFilePath = videoFilePath, exceptionType = ex.GetType().Name, message = ex.Message, stackTrace = ex.StackTrace });

                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                System.Windows.MessageBox.Show(
                                    $"動画の読み込みに失敗しました。\n{ex.Message}",
                                    "エラー",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Error);
                            });
                        }
                    }
                    else
                    {
                        Wideor.App.Shared.Infra.LogHelper.WriteLog(
                            "ShellViewModel.cs:LoadVideoCommand",
                            "File dialog cancelled",
                            null);
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

            // --- 編集コマンドの初期化（テキストエリアに挿入） ---
            // テキストファースト：カーソル位置のセパレータ（パラグラフ）に対してコマンドを実行

            // カーソル位置のセパレータで分割（CUTコマンド）
            SplitAtCurrentPositionCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // エディタのカーソル位置からセパレータ時間範囲を取得
                    var separatorTimeRange = EditorViewModel.CurrentSeparatorTimeRange.Value;
                    if (!separatorTimeRange.HasValue)
                    {
                        System.Windows.MessageBox.Show(
                            "エディタでパラグラフ（セパレータの下の行）にカーソルを置いてください。",
                            "情報",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        return;
                    }

                    // セパレータの中央位置で分割
                    var startTime = separatorTimeRange.Value.startTime;
                    var endTime = separatorTimeRange.Value.endTime;
                    var splitTime = startTime + ((endTime - startTime) / 2);
                    var cutCommand = FormatTimeCommand("CUT", splitTime);
                    AppendCommandToEditor(cutCommand);

                    LogHelper.WriteLog(
                        "ShellViewModel.cs:SplitAtCurrentPositionCommand",
                        "CUT command inserted",
                        new { startTime = startTime, endTime = endTime, splitTime = splitTime });
                })
                .AddTo(_disposables);

            // カーソル位置のセパレータを非表示（HIDEコマンド）
            HideSelectedSegmentCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // エディタのカーソル位置からセパレータ時間範囲を取得
                    var separatorTimeRange = EditorViewModel.CurrentSeparatorTimeRange.Value;
                    if (!separatorTimeRange.HasValue)
                    {
                        System.Windows.MessageBox.Show(
                            "エディタでパラグラフ（セパレータの下の行）にカーソルを置いてください。",
                            "情報",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        return;
                    }

                    var startTime = separatorTimeRange.Value.startTime;
                    var endTime = separatorTimeRange.Value.endTime;
                    var hideCommand = FormatRangeCommand("HIDE", startTime, endTime);
                    AppendCommandToEditor(hideCommand);

                    LogHelper.WriteLog(
                        "ShellViewModel.cs:HideSelectedSegmentCommand",
                        "HIDE command inserted",
                        new { startTime = startTime, endTime = endTime });
                })
                .AddTo(_disposables);

            // カーソル位置のセパレータを表示（SHOWコマンド）
            ShowSelectedSegmentCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // エディタのカーソル位置からセパレータ時間範囲を取得
                    var separatorTimeRange = EditorViewModel.CurrentSeparatorTimeRange.Value;
                    if (!separatorTimeRange.HasValue)
                    {
                        System.Windows.MessageBox.Show(
                            "エディタでパラグラフ（セパレータの下の行）にカーソルを置いてください。",
                            "情報",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        return;
                    }

                    var startTime = separatorTimeRange.Value.startTime;
                    var endTime = separatorTimeRange.Value.endTime;
                    var showCommand = FormatRangeCommand("SHOW", startTime, endTime);
                    AppendCommandToEditor(showCommand);

                    LogHelper.WriteLog(
                        "ShellViewModel.cs:ShowSelectedSegmentCommand",
                        "SHOW command inserted",
                        new { startTime = startTime, endTime = endTime });
                })
                .AddTo(_disposables);

            // カーソル位置のセパレータを削除（DELETEコマンド）
            DeleteSelectedSegmentCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // エディタのカーソル位置からセパレータ時間範囲を取得
                    var separatorTimeRange = EditorViewModel.CurrentSeparatorTimeRange.Value;
                    if (!separatorTimeRange.HasValue)
                    {
                        System.Windows.MessageBox.Show(
                            "エディタでパラグラフ（セパレータの下の行）にカーソルを置いてください。",
                            "情報",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        return;
                    }

                    var startTime = separatorTimeRange.Value.startTime;
                    var endTime = separatorTimeRange.Value.endTime;
                    var deleteCommand = FormatRangeCommand("DELETE", startTime, endTime);
                    AppendCommandToEditor(deleteCommand);

                    LogHelper.WriteLog(
                        "ShellViewModel.cs:DeleteSelectedSegmentCommand",
                        "DELETE command inserted",
                        new { startTime = startTime, endTime = endTime });
                })
                .AddTo(_disposables);

            // カーソル位置のセパレータを複製（パラグラフ対応）
            InsertSeparatorCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // エディタのカーソル位置からセパレータ時間範囲を取得
                    var separatorTimeRange = EditorViewModel.CurrentSeparatorTimeRange.Value;
                    if (!separatorTimeRange.HasValue)
                    {
                        System.Windows.MessageBox.Show(
                            "エディタでパラグラフ（セパレータの下の行）にカーソルを置いてください。",
                            "情報",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        return;
                    }

                    var startTime = separatorTimeRange.Value.startTime;
                    var endTime = separatorTimeRange.Value.endTime;
                    var separator = FormatSeparator(startTime, endTime);
                    AppendCommandToEditor(separator);

                    LogHelper.WriteLog(
                        "ShellViewModel.cs:InsertSeparatorCommand",
                        "Separator inserted",
                        new { startTime = startTime, endTime = endTime });
                })
                .AddTo(_disposables);

            // 現在の再生位置でセパレータを挿入
            InsertSeparatorAtCurrentPositionCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    // 現在の再生位置を取得
                    var currentPosition = _projectContext.CurrentPlaybackPosition.Value;
                    
                    if (currentPosition <= 0)
                    {
                        System.Windows.MessageBox.Show(
                            "動画を再生してから実行してください。",
                            "情報",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        return;
                    }

                    // 現在位置に対応するセグメントを検索
                    var segments = TimelineViewModel.VideoSegments;
                    var currentSegment = segments.FirstOrDefault(s => 
                        s.StartTime <= currentPosition && s.EndTime > currentPosition);
                    
                    if (currentSegment == null)
                    {
                        System.Windows.MessageBox.Show(
                            "現在位置に対応するセグメントが見つかりません。",
                            "エラー",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                        return;
                    }

                    // セグメントの開始位置から現在位置までのセパレータを挿入
                    var separator = FormatSeparator(currentSegment.StartTime, currentPosition);
                    AppendCommandToEditor(separator);

                    LogHelper.WriteLog(
                        "ShellViewModel.cs:InsertSeparatorAtCurrentPositionCommand",
                        "Separator inserted at current position",
                        new { currentPosition = currentPosition, segmentId = currentSegment.Id });
                })
                .AddTo(_disposables);

            // --- EditorとTimelineの連携（セパレータ→セグメント選択） ---

            // EditorViewModelのCurrentSeparatorTimeRangeを監視
            EditorViewModel.CurrentSeparatorTimeRange
                .Subscribe(timeRange =>
                {
                    if (timeRange.HasValue)
                    {
                        // 時間範囲に対応するVideoSegmentを検索
                        var segment = FindSegmentByTimeRange(timeRange.Value.startTime, timeRange.Value.endTime);
                        
                        if (segment != null)
                        {
                            TimelineViewModel.SelectedSegment.Value = segment;
                            
                            LogHelper.WriteLog(
                                "ShellViewModel.cs:EditorToTimelineLinking",
                                "Segment selected from separator",
                                new { 
                                    segmentId = segment.Id, 
                                    startTime = timeRange.Value.startTime, 
                                    endTime = timeRange.Value.endTime 
                                });
                        }
                        else
                        {
                            // 対応するセグメントが見つからない場合は選択解除
                            TimelineViewModel.SelectedSegment.Value = null;
                            
                            LogHelper.WriteLog(
                                "ShellViewModel.cs:EditorToTimelineLinking",
                                "No segment found for separator",
                                new { 
                                    startTime = timeRange.Value.startTime, 
                                    endTime = timeRange.Value.endTime 
                                });
                        }
                    }
                    else
                    {
                        // セパレータがない場合は選択解除
                        TimelineViewModel.SelectedSegment.Value = null;
                    }
                })
                .AddTo(_disposables);

            // 初期化処理を開始
            _ = InitializeAsync();
        }

        /// <summary>
        /// EditorのテキストにLOADコマンドを自動追加
        /// </summary>
        private void AddLoadCommandToEditor(string videoFilePath)
        {
            try
            {
                // 現在のテキストを取得
                var currentText = EditorViewModel.Text.Value ?? string.Empty;

                // LOADコマンドを生成
                var loadCommandText = FormatLoadCommand(videoFilePath);

                // Headerセクションを探す
                var lines = currentText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var headerEndIndex = -1;

                // "---" を探してHeaderの終わりを見つける
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == "---")
                    {
                        headerEndIndex = i;
                        break;
                    }
                }

                string newText;
                if (headerEndIndex >= 0)
                {
                    // Headerが存在する場合、その直後にLOADコマンドを追加
                    var headerLines = lines.Take(headerEndIndex + 1).ToList();
                    var bodyLines = lines.Skip(headerEndIndex + 1).ToList();

                    // 既存のLOADコマンドがあるかチェック
                    var existingLoadIndex = bodyLines.FindIndex(line => 
                        line.TrimStart().StartsWith("LOAD ", StringComparison.OrdinalIgnoreCase));

                    if (existingLoadIndex >= 0)
                    {
                        // 既存のLOADコマンドを置き換え
                        bodyLines[existingLoadIndex] = loadCommandText;
                    }
                    else
                    {
                        // 新しいLOADコマンドを追加（空行を挟む）
                        if (bodyLines.Count > 0 && !string.IsNullOrWhiteSpace(bodyLines[0]))
                        {
                            bodyLines.Insert(0, string.Empty);
                        }
                        bodyLines.Insert(0, loadCommandText);
                    }

                    newText = string.Join(Environment.NewLine, headerLines.Concat(bodyLines));
                }
                else
                {
                    // Headerが存在しない場合、先頭に追加
                    if (!string.IsNullOrWhiteSpace(currentText))
                    {
                        newText = loadCommandText + Environment.NewLine + Environment.NewLine + currentText;
                    }
                    else
                    {
                        newText = loadCommandText;
                    }
                }

                // EditorViewModelのテキストを更新
                EditorViewModel.Text.Value = newText;

                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "ShellViewModel:AddLoadCommandToEditor",
                    "LOAD command added to editor",
                    new { videoFilePath = videoFilePath, commandText = loadCommandText });
            }
            catch (Exception ex)
            {
                Wideor.App.Shared.Infra.LogHelper.WriteLog(
                    "ShellViewModel:AddLoadCommandToEditor",
                    "Failed to add LOAD command to editor",
                    new { videoFilePath = videoFilePath, error = ex.Message });
            }
        }

        /// <summary>
        /// LOADコマンドのテキストをフォーマット
        /// </summary>
        private string FormatLoadCommand(string filePath)
        {
            // パスにスペースが含まれている場合はクォートで囲む
            if (filePath.Contains(" "))
            {
                return $"LOAD \"{filePath}\"";
            }
            else
            {
                return $"LOAD {filePath}";
            }
        }

        /// <summary>
        /// 時間コマンド（CUTなど）のテキストをフォーマット
        /// </summary>
        private string FormatTimeCommand(string command, double timeSeconds)
        {
            var timeSpan = TimeSpan.FromSeconds(timeSeconds);
            return $"{command} {timeSpan:hh\\:mm\\:ss\\.fff}";
        }

        /// <summary>
        /// 範囲コマンド（HIDE, SHOW, DELETEなど）のテキストをフォーマット
        /// </summary>
        private string FormatRangeCommand(string command, double startSeconds, double endSeconds)
        {
            var startTime = TimeSpan.FromSeconds(startSeconds);
            var endTime = TimeSpan.FromSeconds(endSeconds);
            return $"{command} {startTime:hh\\:mm\\:ss\\.fff} {endTime:hh\\:mm\\:ss\\.fff}";
        }

        /// <summary>
        /// セパレータ（パラグラフ対応）のテキストをフォーマット
        /// </summary>
        private string FormatSeparator(double startSeconds, double endSeconds)
        {
            var startTime = TimeSpan.FromSeconds(startSeconds);
            var endTime = TimeSpan.FromSeconds(endSeconds);
            return $"--- [{startTime:hh\\:mm\\:ss\\.fff} -> {endTime:hh\\:mm\\:ss\\.fff}] ---";
        }

        /// <summary>
        /// エディタにコマンドを追加
        /// </summary>
        private void AppendCommandToEditor(string command)
        {
            try
            {
                var currentText = EditorViewModel.Text.Value ?? string.Empty;
                
                // 末尾に改行がない場合は追加
                if (!currentText.EndsWith("\n") && !string.IsNullOrEmpty(currentText))
                {
                    currentText += "\n";
                }
                
                // コマンドを追加
                var newText = currentText + command + "\n";
                
                EditorViewModel.Text.Value = newText;
                
                LogHelper.WriteLog(
                    "ShellViewModel.cs:AppendCommandToEditor",
                    "Command appended to editor",
                    new { command = command });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "ShellViewModel.cs:AppendCommandToEditor",
                    "Error appending command",
                    new { command = command, error = ex.Message });
            }
        }

        /// <summary>
        /// 時間範囲に対応するVideoSegmentを検索
        /// </summary>
        private VideoSegment? FindSegmentByTimeRange(double startTime, double endTime)
        {
            try
            {
                var segments = TimelineViewModel.VideoSegments;
                if (segments == null || segments.Count == 0)
                    return null;

                // 完全一致するセグメントを検索
                const double tolerance = 0.01; // 10ms以内の誤差を許容
                var exactMatch = segments.FirstOrDefault(s => 
                    Math.Abs(s.StartTime - startTime) < tolerance && 
                    Math.Abs(s.EndTime - endTime) < tolerance);

                if (exactMatch != null)
                    return exactMatch;

                // 完全一致がない場合、範囲が重なるセグメントを検索
                var overlappingSegment = segments.FirstOrDefault(s =>
                    s.StartTime < endTime && s.EndTime > startTime);

                return overlappingSegment;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "ShellViewModel.cs:FindSegmentByTimeRange",
                    "Error finding segment",
                    new { startTime = startTime, endTime = endTime, error = ex.Message });
                return null;
            }
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
