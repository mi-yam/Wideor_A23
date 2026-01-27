using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Features.Editor.Internal;
using Wideor.App.Shared.Domain;
using Wideor.App.Shared.Infra;

// SceneParserの曖昧さを解消するためのエイリアス
using InternalSceneParser = Wideor.App.Features.Editor.Internal.SceneParser;

namespace Wideor.App.Features.Editor
{
    /// <summary>
    /// エディタ機能のViewModel。
    /// テキストの変更をIProjectContextに反映し、アンカーボタンのロジックを管理します。
    /// Header/Bodyパース機能とCommandExecutorとの統合も行います。
    /// </summary>
    public class EditorViewModel : IDisposable
    {
        private readonly IProjectContext _projectContext;
        private readonly IScrollCoordinator _scrollCoordinator;
        private readonly IHeaderParser _headerParser;
        private readonly ISceneParser _sceneParser;
        private readonly ICommandParser _commandParser;
        private readonly ICommandExecutor _commandExecutor;
        private readonly IVideoSegmentManager _segmentManager;
        private readonly AnchorLogic _anchorLogic;
        private readonly CompositeDisposable _disposables = new();

        // --- Reactive Properties ---

        /// <summary>
        /// エディタのテキスト内容
        /// </summary>
        public ReactiveProperty<string> Text { get; }

        /// <summary>
        /// プロジェクト設定（Headerから生成）
        /// </summary>
        public ReactiveProperty<ProjectConfig> ProjectConfig { get; }

        /// <summary>
        /// アンカーボタンの状態（未確定）
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsAnchorPending { get; }

        /// <summary>
        /// アンカーボタンの状態（確定）
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsAnchorConfirmed { get; }

        /// <summary>
        /// アンカーボタンのツールチップテキスト
        /// </summary>
        public IReadOnlyReactiveProperty<string> AnchorToolTip { get; }

        /// <summary>
        /// 現在の再生位置（アンカー設定用）
        /// </summary>
        public IReadOnlyReactiveProperty<double> CurrentPlaybackPosition { get; }

        /// <summary>
        /// シーンブロックのリスト（TimelineViewModelで使用）
        /// </summary>
        public IReadOnlyReactiveProperty<IReadOnlyList<SceneBlock>> SceneBlocks { get; }

        /// <summary>
        /// エディタのカーソル位置（文字オフセット）
        /// </summary>
        public ReactiveProperty<int> CaretPosition { get; }

        /// <summary>
        /// カーソル位置に対応するセパレータの時間範囲
        /// </summary>
        public IReadOnlyReactiveProperty<(double startTime, double endTime)?> CurrentSeparatorTimeRange { get; }

        private readonly ReactiveProperty<(double startTime, double endTime)?> _currentSeparatorTimeRange;

        /// <summary>
        /// ScrollCoordinator（ViewからScrollViewerを登録するために公開）
        /// </summary>
        public IScrollCoordinator ScrollCoordinator => _scrollCoordinator;

        // --- Commands ---

        /// <summary>
        /// アンカーボタンのクリックコマンド
        /// </summary>
        public ReactiveCommand AnchorClickCommand { get; }

        private string _lastProcessedText = string.Empty;
        private string _previousCommandsHash = string.Empty;
        private bool _isParsing = false;

        public EditorViewModel(
            IProjectContext projectContext,
            IScrollCoordinator scrollCoordinator,
            IHeaderParser? headerParser = null,
            ISceneParser? sceneParser = null,
            ICommandParser? commandParser = null,
            ICommandExecutor? commandExecutor = null,
            IVideoSegmentManager? segmentManager = null)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
            _scrollCoordinator = scrollCoordinator ?? throw new ArgumentNullException(nameof(scrollCoordinator));
            
            // オプショナルな依存関係（DI未設定時はnull）
            _headerParser = headerParser ?? new HeaderParser();
            _sceneParser = sceneParser ?? new Wideor.App.Shared.Infra.SceneParser();
            _commandParser = commandParser!;
            _commandExecutor = commandExecutor!;
            _segmentManager = segmentManager!;
            
            _anchorLogic = new AnchorLogic();

            // プロパティの初期化
            Text = new ReactiveProperty<string>(string.Empty)
                .AddTo(_disposables);

            // プロジェクト設定の初期化
            ProjectConfig = new ReactiveProperty<ProjectConfig>(new ProjectConfig())
                .AddTo(_disposables);

            CurrentPlaybackPosition = _projectContext.CurrentPlaybackPosition
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);

            // SceneBlocksプロパティを初期化（IProjectContextのSceneBlocksを変換）
            SceneBlocks = _projectContext.SceneBlocks
                .Select(blocks => blocks != null ? (IReadOnlyList<SceneBlock>)blocks.ToList() : Array.Empty<SceneBlock>())
                .ToReadOnlyReactiveProperty<IReadOnlyList<SceneBlock>>(Array.Empty<SceneBlock>())
                .AddTo(_disposables);

            // カーソル位置の初期化
            CaretPosition = new ReactiveProperty<int>(0)
                .AddTo(_disposables);

            // カーソル位置に対応するセパレータ時間範囲の初期化
            _currentSeparatorTimeRange = new ReactiveProperty<(double startTime, double endTime)?>(default((double, double)?))
                .AddTo(_disposables);
            
            CurrentSeparatorTimeRange = _currentSeparatorTimeRange
                .ToReadOnlyReactiveProperty<(double startTime, double endTime)?>(default((double, double)?))
                .AddTo(_disposables);

            // カーソル位置またはテキストが変更されたらセパレータを解析
            Observable.CombineLatest(
                CaretPosition,
                Text,
                (caret, text) => (caret, text))
                .Throttle(TimeSpan.FromMilliseconds(100)) // デバウンス
                .Subscribe(x => UpdateCurrentSeparator(x.caret, x.text))
                .AddTo(_disposables);

            IsAnchorPending = _anchorLogic.IsRecording
                .ToReadOnlyReactiveProperty(false)
                .AddTo(_disposables);

            IsAnchorConfirmed = _anchorLogic.PivotTime
                .Select(pivot => pivot != null)
                .ToReadOnlyReactiveProperty(false)
                .AddTo(_disposables);

            AnchorToolTip = _anchorLogic.IsRecording
                .CombineLatest(_anchorLogic.PivotTime, (recording, pivot) =>
                {
                    if (recording && pivot.HasValue)
                    {
                        return $"録画中... ピボット: {FormatTime(pivot.Value)}";
                    }
                    return "アンカーを設定";
                })
                .ToReadOnlyReactiveProperty("アンカーを設定")
                .AddTo(_disposables);

            // アンカーボタンのクリックコマンド
            AnchorClickCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    var currentTime = CurrentPlaybackPosition.Value;
                    var wasRecording = _anchorLogic.IsRecording.Value;
                    var previousPivot = _anchorLogic.PivotTime.Value;

                    _anchorLogic.SetPivot(currentTime);

                    // 2回目のクリックで確定された場合、テキストに挿入
                    if (wasRecording && previousPivot.HasValue && !_anchorLogic.IsRecording.Value)
                    {
                        var (start, end) = _anchorLogic.Confirm(currentTime);
                        InsertTimeCommand(start, end);
                    }
                })
                .AddTo(_disposables);

            // テキスト変更を監視してIProjectContextに反映（デバウンス）
            Text
                .Skip(1) // 初期値はスキップ
                .Throttle(TimeSpan.FromMilliseconds(500)) // 500ms待機
                .Subscribe(text => ProcessTextChange(text))
                .AddTo(_disposables);

            // プロジェクトが読み込まれたらテキストを更新
            _projectContext.IsProjectLoaded
                .Where(loaded => loaded)
                .Subscribe(_ => LoadTextFromProject())
                .AddTo(_disposables);

            // 現在の再生位置が変更されたら、プレビュー範囲を更新
            CurrentPlaybackPosition
                .Where(_ => _anchorLogic.IsRecording.Value)
                .Subscribe(currentTime =>
                {
                    _anchorLogic.CalculatePreviewRange(currentTime);
                })
                .AddTo(_disposables);
        }

        /// <summary>
        /// テキスト変更を処理してIProjectContextに反映
        /// Header/Bodyパース機能とCommandExecutorとの統合を行います。
        /// </summary>
        private void ProcessTextChange(string text)
        {
            if (text == _lastProcessedText)
                return;

            // 再帰的な更新を防ぐ
            if (_isParsing)
                return;

            _lastProcessedText = text;

            try
            {
                _isParsing = true;

                LogHelper.WriteLog(
                    "EditorViewModel:ProcessTextChange",
                    "Text changed, starting parse",
                    new { textLength = text.Length });

                // ステップ1: Headerをパース
                var (projectConfig, bodyStartLine) = _headerParser.ParseHeader(text);
                
                // プロジェクト設定を更新
                ProjectConfig.Value = projectConfig;

                LogHelper.WriteLog(
                    "EditorViewModel:ProcessTextChange",
                    "Header parsed",
                    new { projectName = projectConfig.ProjectName, bodyStartLine = bodyStartLine });

                // ステップ2: Body部分のテキストを抽出
                var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var bodyLines = lines.Skip(bodyStartLine);
                var bodyText = string.Join("\n", bodyLines);

                // ステップ3: Bodyのコマンドをパース（CommandParserが利用可能な場合）
                if (_commandParser != null)
                {
                    var commands = _commandParser.ParseCommands(bodyText);

                    // コマンドのハッシュ値を計算（差分検出）
                    var currentHash = CalculateCommandsHash(commands);

                    // 前回と異なる場合のみ実行
                    if (currentHash != _previousCommandsHash)
                    {
                        _previousCommandsHash = currentHash;

                        // セグメントマネージャーをクリアして再構築
                        if (_segmentManager != null)
                        {
                            _segmentManager.Clear();
                        }

                        // コマンドを実行
                        if (_commandExecutor != null)
                        {
                            _commandExecutor.ExecuteCommands(commands);
                        }

                        LogHelper.WriteLog(
                            "EditorViewModel:ProcessTextChange",
                            "Commands executed",
                            new { commandCount = commands.Count });
                    }
                }

                // ステップ4: Bodyのシーン（パラグラフ）をパース
                var scenes = _sceneParser.ParseScenes(bodyText);

                // IProjectContextのSceneBlocksを更新
                var existingScenes = _projectContext.SceneBlocks.Value?.ToList() ?? new List<SceneBlock>();
                foreach (var scene in existingScenes)
                {
                    _projectContext.RemoveSceneBlock(scene.Id);
                }

                // 新しいシーンブロックを追加
                foreach (var scene in scenes)
                {
                    _projectContext.AddSceneBlock(scene);
                }

                LogHelper.WriteLog(
                    "EditorViewModel:ProcessTextChange",
                    "Parse completed",
                    new { sceneCount = scenes.Count });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "EditorViewModel:ProcessTextChange",
                    "Error during parse",
                    new { exceptionType = ex.GetType().Name, message = ex.Message });
                
                System.Diagnostics.Debug.WriteLine($"テキスト解析エラー: {ex.Message}");
            }
            finally
            {
                _isParsing = false;
            }
        }

        /// <summary>
        /// コマンドリストからハッシュ値を計算（差分検出用）
        /// </summary>
        private string CalculateCommandsHash(List<EditCommand> commands)
        {
            var commandStrings = commands.Select(cmd =>
            {
                return cmd.Type switch
                {
                    CommandType.Load => $"LOAD:{cmd.FilePath}",
                    CommandType.Cut => $"CUT:{cmd.Time}",
                    CommandType.Hide => $"HIDE:{cmd.StartTime}:{cmd.EndTime}",
                    CommandType.Show => $"SHOW:{cmd.StartTime}:{cmd.EndTime}",
                    CommandType.Delete => $"DELETE:{cmd.StartTime}:{cmd.EndTime}",
                    _ => string.Empty
                };
            });

            var combined = string.Join("|", commandStrings);

            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                return Convert.ToBase64String(hashBytes);
            }
        }

        /// <summary>
        /// プロジェクトからテキストを読み込む
        /// </summary>
        private void LoadTextFromProject()
        {
            var scenes = _projectContext.SceneBlocks.Value;
            if (scenes == null || scenes.Count == 0)
            {
                Text.Value = string.Empty;
                return;
            }

            var textBuilder = new System.Text.StringBuilder();
            foreach (var scene in scenes.OrderBy(s => s.StartTime))
            {
                // コマンド行を生成
                var command = InternalSceneParser.GenerateCommandText(scene.StartTime, scene.EndTime);
                textBuilder.AppendLine(command);

                // タイトルを追加
                if (!string.IsNullOrWhiteSpace(scene.Title))
                {
                    textBuilder.AppendLine(scene.Title);
                }

                textBuilder.AppendLine();
            }

            Text.Value = textBuilder.ToString().TrimEnd();
            _lastProcessedText = Text.Value;
        }

        /// <summary>
        /// 時間コマンドをテキストに挿入
        /// </summary>
        private void InsertTimeCommand(double startTime, double endTime)
        {
            var command = InternalSceneParser.GenerateCommandText(startTime, endTime);
            var currentText = Text.Value;
            var cursorPosition = 0; // 実際のカーソル位置はEditorViewから取得する必要がある

            // カーソル位置にコマンドを挿入
            var newText = currentText.Insert(cursorPosition, command + Environment.NewLine);
            Text.Value = newText;
        }

        /// <summary>
        /// 時間を文字列にフォーマット
        /// </summary>
        private string FormatTime(double seconds)
        {
            var timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            else
            {
                return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
        }

        /// <summary>
        /// カーソル位置からセパレータブロックを解析して時間範囲を取得
        /// </summary>
        private void UpdateCurrentSeparator(int caretPosition, string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || caretPosition < 0 || caretPosition > text.Length)
                {
                    _currentSeparatorTimeRange.Value = null;
                    return;
                }

                // セパレータの正規表現パターン
                // 形式: --- [00:00:00.000 -> 00:00:10.000] ---
                var separatorPattern = @"^[-]{3,}\s*\[(?<start>\d{2}:\d{2}:\d{2}\.\d{3})\s*->\s*(?<end>\d{2}:\d{2}:\d{2}\.\d{3})\]\s*[-]{3,}$";
                var regex = new System.Text.RegularExpressions.Regex(separatorPattern, System.Text.RegularExpressions.RegexOptions.Multiline);

                // テキストを行に分割
                var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                
                // カーソル位置が何行目かを特定
                int currentLineIndex = 0;
                int charCount = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (charCount + lines[i].Length >= caretPosition)
                    {
                        currentLineIndex = i;
                        break;
                    }
                    // 改行文字分を加算（Windows: \r\n = 2, Unix: \n = 1）
                    charCount += lines[i].Length + (text.Contains("\r\n") ? 2 : 1);
                }

                // カーソル行から上方向にセパレータを探す
                (double startTime, double endTime)? foundRange = null;
                for (int i = currentLineIndex; i >= 0; i--)
                {
                    var match = regex.Match(lines[i]);
                    if (match.Success)
                    {
                        var startTimeStr = match.Groups["start"].Value;
                        var endTimeStr = match.Groups["end"].Value;
                        
                        if (TimeSpan.TryParseExact(startTimeStr, @"hh\:mm\:ss\.fff", null, out var startTs) &&
                            TimeSpan.TryParseExact(endTimeStr, @"hh\:mm\:ss\.fff", null, out var endTs))
                        {
                            foundRange = (startTs.TotalSeconds, endTs.TotalSeconds);
                            
                            LogHelper.WriteLog(
                                "EditorViewModel.cs:UpdateCurrentSeparator",
                                "Found separator for cursor position",
                                new { 
                                    caretPosition = caretPosition, 
                                    lineIndex = i, 
                                    startTime = startTs.TotalSeconds, 
                                    endTime = endTs.TotalSeconds 
                                });
                            break;
                        }
                    }
                    
                    // 空行や他のセパレータ（時間なし）を見つけたら、それより上のセパレータは対象外
                    if (string.IsNullOrWhiteSpace(lines[i]) && i < currentLineIndex)
                    {
                        // 空行はスキップ
                        continue;
                    }
                }

                _currentSeparatorTimeRange.Value = foundRange;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "EditorViewModel.cs:UpdateCurrentSeparator",
                    "Error parsing separator",
                    new { caretPosition = caretPosition, error = ex.Message });
                _currentSeparatorTimeRange.Value = null;
            }
        }

        public void Dispose()
        {
            _anchorLogic?.Dispose();
            _disposables?.Dispose();
        }
    }
}
