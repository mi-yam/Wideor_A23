using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Features.Editor.Internal;
using Wideor.App.Shared.Domain;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Features.Editor
{
    /// <summary>
    /// エディタ機能のViewModel。
    /// テキストの変更をIProjectContextに反映し、アンカーボタンのロジックを管理します。
    /// </summary>
    public class EditorViewModel : IDisposable
    {
        private readonly IProjectContext _projectContext;
        private readonly IScrollCoordinator _scrollCoordinator;
        private readonly AnchorLogic _anchorLogic;
        private readonly CompositeDisposable _disposables = new();

        // --- Reactive Properties ---

        /// <summary>
        /// エディタのテキスト内容
        /// </summary>
        public ReactiveProperty<string> Text { get; }

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
        /// ScrollCoordinator（ViewからScrollViewerを登録するために公開）
        /// </summary>
        public IScrollCoordinator ScrollCoordinator => _scrollCoordinator;

        // --- Commands ---

        /// <summary>
        /// アンカーボタンのクリックコマンド
        /// </summary>
        public ReactiveCommand AnchorClickCommand { get; }

        private readonly System.Threading.Timer? _textChangeTimer;
        private string _lastProcessedText = string.Empty;

        public EditorViewModel(
            IProjectContext projectContext,
            IScrollCoordinator scrollCoordinator)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
            _scrollCoordinator = scrollCoordinator ?? throw new ArgumentNullException(nameof(scrollCoordinator));
            _anchorLogic = new AnchorLogic();

            // プロパティの初期化
            Text = new ReactiveProperty<string>(string.Empty)
                .AddTo(_disposables);

            CurrentPlaybackPosition = _projectContext.CurrentPlaybackPosition
                .ToReadOnlyReactiveProperty(0.0)
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
        /// </summary>
        private void ProcessTextChange(string text)
        {
            if (text == _lastProcessedText)
                return;

            _lastProcessedText = text;

            try
            {
                // テキストからSceneBlockを生成
                var scenes = SceneParser.Parse(text);

                // IProjectContextのSceneBlocksを更新
                // 既存のシーンブロックをすべて削除してから追加
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
            }
            catch (Exception ex)
            {
                // エラーハンドリング（必要に応じてログ出力など）
                System.Diagnostics.Debug.WriteLine($"テキスト解析エラー: {ex.Message}");
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
                var command = SceneParser.GenerateCommandText(scene.StartTime, scene.EndTime);
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
            var command = SceneParser.GenerateCommandText(startTime, endTime);
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

        public void Dispose()
        {
            _textChangeTimer?.Dispose();
            _anchorLogic?.Dispose();
            _disposables?.Dispose();
        }
    }
}
