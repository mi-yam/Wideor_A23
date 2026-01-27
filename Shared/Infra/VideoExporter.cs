using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;
using Xabe.FFmpeg;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// FFmpegを使用した動画書き出し機能の実装
    /// Xabe.FFmpegライブラリを使用し、FFmpegの自動ダウンロードに対応しています。
    /// </summary>
    public class VideoExporter : IVideoExporter
    {
        private CancellationTokenSource? _currentCancellation;
        private bool _isCancelling;

        public bool CanCancel => _currentCancellation != null && !_currentCancellation.IsCancellationRequested;

        /// <summary>
        /// FFmpegが利用可能かどうかを確認します。
        /// 必要に応じて自動的にダウンロードします。
        /// </summary>
        public async Task<bool> IsFFmpegAvailableAsync()
        {
            // まず既存のインストールをチェック
            if (FFmpegManager.CheckAvailability())
            {
                return true;
            }

            // FFmpegをダウンロード
            var result = await FFmpegManager.InitializeAsync();
            return result;
        }

        /// <summary>
        /// 動画を書き出します。
        /// </summary>
        public async Task<bool> ExportAsync(
            IReadOnlyList<VideoSegment> segments,
            IReadOnlyList<SceneBlock> sceneBlocks,
            ProjectConfig config,
            string outputPath,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _isCancelling = false;
            _currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var startTime = DateTime.Now;

            try
            {
                // FFmpegの初期化
                progress?.Report(new ExportProgress
                {
                    Progress = 0,
                    CurrentStep = ExportStep.Preparing,
                    Message = "FFmpegを準備中...",
                    Elapsed = TimeSpan.Zero,
                    CurrentSegmentIndex = 0,
                    TotalSegments = segments.Count
                });

                var ffmpegAvailable = await IsFFmpegAvailableAsync();
                if (!ffmpegAvailable)
                {
                    LogHelper.WriteLog(
                        "VideoExporter:ExportAsync",
                        "FFmpeg not available",
                        null);
                    
                    progress?.Report(new ExportProgress
                    {
                        Progress = 0,
                        CurrentStep = ExportStep.Error,
                        Message = "FFmpegの準備に失敗しました",
                        Elapsed = DateTime.Now - startTime
                    });
                    return false;
                }

                progress?.Report(new ExportProgress
                {
                    Progress = 0.05,
                    CurrentStep = ExportStep.Preparing,
                    Message = "準備中...",
                    Elapsed = DateTime.Now - startTime,
                    CurrentSegmentIndex = 0,
                    TotalSegments = segments.Count
                });

                // 表示されているセグメントのみをフィルタリング（開始時間順にソート）
                var visibleSegments = segments
                    .Where(s => s.Visible && s.State != SegmentState.Hidden)
                    .OrderBy(s => s.StartTime)
                    .ToList();

                if (visibleSegments.Count == 0)
                {
                    LogHelper.WriteLog(
                        "VideoExporter:ExportAsync",
                        "No visible segments to export",
                        null);
                    return false;
                }

                // 一時ディレクトリを作成
                var tempDir = Path.Combine(Path.GetTempPath(), $"wideor_export_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // 中間ファイルのリスト
                    var intermediateFiles = new List<string>();

                    // 各セグメントを処理
                    for (int i = 0; i < visibleSegments.Count; i++)
                    {
                        if (_currentCancellation.IsCancellationRequested || _isCancelling)
                        {
                            return false;
                        }

                        var segment = visibleSegments[i];
                        var elapsed = DateTime.Now - startTime;
                        var estimatedTotal = i > 0 ? TimeSpan.FromTicks(elapsed.Ticks * visibleSegments.Count / i) : (TimeSpan?)null;
                        var estimatedRemaining = estimatedTotal.HasValue ? estimatedTotal.Value - elapsed : (TimeSpan?)null;

                        progress?.Report(new ExportProgress
                        {
                            Progress = 0.05 + ((double)i / visibleSegments.Count * 0.75), // 5%～80%
                            CurrentStep = ExportStep.ProcessingSegments,
                            Message = $"セグメント {i + 1}/{visibleSegments.Count} を処理中...",
                            Elapsed = elapsed,
                            EstimatedRemaining = estimatedRemaining,
                            CurrentSegmentIndex = i + 1,
                            TotalSegments = visibleSegments.Count
                        });

                        // セグメントに対応するシーンブロックを検索
                        var matchingScene = FindMatchingSceneBlock(segment, sceneBlocks);

                        // 中間ファイルを生成
                        var intermediateFile = Path.Combine(tempDir, $"segment_{i:D4}.mp4");
                        var success = await ProcessSegmentWithXabeAsync(
                            segment,
                            matchingScene,
                            config,
                            intermediateFile,
                            _currentCancellation.Token);

                        if (!success)
                        {
                            LogHelper.WriteLog(
                                "VideoExporter:ExportAsync",
                                "Failed to process segment",
                                new { segmentId = segment.Id, index = i });
                            return false;
                        }

                        intermediateFiles.Add(intermediateFile);
                    }

                    if (_currentCancellation.IsCancellationRequested || _isCancelling)
                    {
                        return false;
                    }

                    progress?.Report(new ExportProgress
                    {
                        Progress = 0.85,
                        CurrentStep = ExportStep.Concatenating,
                        Message = "動画を結合中...",
                        Elapsed = DateTime.Now - startTime,
                        CurrentSegmentIndex = visibleSegments.Count,
                        TotalSegments = visibleSegments.Count
                    });

                    // 動画を結合
                    var concatSuccess = await ConcatenateVideosWithXabeAsync(
                        intermediateFiles,
                        outputPath,
                        config,
                        _currentCancellation.Token);

                    if (!concatSuccess)
                    {
                        LogHelper.WriteLog(
                            "VideoExporter:ExportAsync",
                            "Failed to concatenate videos",
                            null);
                        return false;
                    }

                    progress?.Report(new ExportProgress
                    {
                        Progress = 1.0,
                        CurrentStep = ExportStep.Completed,
                        Message = "エクスポート完了",
                        Elapsed = DateTime.Now - startTime,
                        CurrentSegmentIndex = visibleSegments.Count,
                        TotalSegments = visibleSegments.Count
                    });

                    LogHelper.WriteLog(
                        "VideoExporter:ExportAsync",
                        "Export completed successfully",
                        new { outputPath = outputPath, segmentCount = visibleSegments.Count, elapsed = (DateTime.Now - startTime).TotalSeconds });

                    return true;
                }
                finally
                {
                    // 一時ディレクトリを削除
                    try
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLog(
                            "VideoExporter:ExportAsync",
                            "Failed to delete temp directory",
                            new { tempDir = tempDir, error = ex.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "VideoExporter:ExportAsync",
                    "Export failed with exception",
                    new { error = ex.Message, stackTrace = ex.StackTrace });

                progress?.Report(new ExportProgress
                {
                    Progress = 0,
                    CurrentStep = ExportStep.Error,
                    Message = $"エラー: {ex.Message}",
                    Elapsed = DateTime.Now - startTime
                });

                return false;
            }
            finally
            {
                _currentCancellation = null;
            }
        }

        /// <summary>
        /// セグメントに対応するシーンブロックを検索
        /// </summary>
        private SceneBlock? FindMatchingSceneBlock(VideoSegment segment, IReadOnlyList<SceneBlock> sceneBlocks)
        {
            const double tolerance = 0.1; // 100msの許容誤差

            // 時間範囲が重なるすべてのシーンブロックを取得
            var overlappingBlocks = sceneBlocks
                .Where(s => s.StartTime < segment.EndTime && s.EndTime > segment.StartTime)
                .ToList();

            if (overlappingBlocks.Count == 0)
            {
                LogHelper.WriteLog(
                    "VideoExporter:FindMatchingSceneBlock",
                    "No overlapping scene blocks found",
                    new { segmentId = segment.Id, segmentStart = segment.StartTime, segmentEnd = segment.EndTime });
                return null;
            }

            LogHelper.WriteLog(
                "VideoExporter:FindMatchingSceneBlock",
                "Found overlapping scene blocks",
                new { 
                    segmentId = segment.Id, 
                    overlappingCount = overlappingBlocks.Count,
                    blocks = overlappingBlocks.Select(b => new { 
                        b.Id, 
                        b.StartTime, 
                        b.EndTime, 
                        b.Title, 
                        b.Subtitle, 
                        freeTextCount = b.FreeTextItems?.Count ?? 0 
                    }).ToList()
                });

            // コンテンツがあるシーンブロックを優先して選択
            // 優先順位: タイトル+字幕+FreeText > タイトル+字幕 > タイトル > 字幕 > FreeText > なし
            var bestMatch = overlappingBlocks
                .OrderByDescending(s => GetContentScore(s))
                .ThenBy(s => Math.Abs(s.StartTime - segment.StartTime)) // 開始時間が近いものを優先
                .FirstOrDefault();

            if (bestMatch != null)
            {
                LogHelper.WriteLog(
                    "VideoExporter:FindMatchingSceneBlock",
                    "Selected best matching scene block",
                    new { 
                        segmentId = segment.Id, 
                        sceneId = bestMatch.Id,
                        title = bestMatch.Title,
                        subtitle = bestMatch.Subtitle,
                        freeTextCount = bestMatch.FreeTextItems?.Count ?? 0
                    });
            }

            return bestMatch;
        }

        /// <summary>
        /// シーンブロックのコンテンツスコアを計算
        /// </summary>
        private int GetContentScore(SceneBlock sceneBlock)
        {
            int score = 0;
            if (!string.IsNullOrWhiteSpace(sceneBlock.Title))
                score += 4;
            if (!string.IsNullOrWhiteSpace(sceneBlock.Subtitle))
                score += 2;
            if (sceneBlock.FreeTextItems != null && sceneBlock.FreeTextItems.Count > 0)
                score += 1;
            return score;
        }

        /// <summary>
        /// Xabe.FFmpegを使用してセグメントを処理（切り出し＋テロップ合成）
        /// </summary>
        private async Task<bool> ProcessSegmentWithXabeAsync(
            VideoSegment segment,
            SceneBlock? sceneBlock,
            ProjectConfig config,
            string outputPath,
            CancellationToken cancellationToken)
        {
            try
            {
                LogHelper.WriteLog(
                    "VideoExporter:ProcessSegmentWithXabeAsync",
                    "Processing segment with scene block",
                    new { 
                        segmentId = segment.Id,
                        hasSceneBlock = sceneBlock != null,
                        sceneBlockId = sceneBlock?.Id,
                        sceneBlockTitle = sceneBlock?.Title,
                        sceneBlockSubtitle = sceneBlock?.Subtitle,
                        freeTextCount = sceneBlock?.FreeTextItems?.Count ?? 0
                    });

                // フィルターを構築
                var filters = new List<string>();

                // 解像度を設定
                filters.Add($"scale={config.ResolutionWidth}:{config.ResolutionHeight}:force_original_aspect_ratio=decrease");
                filters.Add($"pad={config.ResolutionWidth}:{config.ResolutionHeight}:(ow-iw)/2:(oh-ih)/2");

                // テロップを追加
                if (sceneBlock != null)
                {
                    AddTextOverlayFilters(filters, sceneBlock, config);
                }

                // FFmpegコマンドを直接構築
                var arguments = new StringBuilder();
                
                // 上書き許可（-yを最初に指定）
                arguments.Append("-y ");
                
                // 入力ファイルの開始位置
                arguments.Append($"-ss {segment.StartTime.ToString("F3", CultureInfo.InvariantCulture)} ");
                
                // 入力ファイル
                arguments.Append($"-i \"{segment.VideoFilePath}\" ");
                
                // 長さ
                arguments.Append($"-t {segment.Duration.ToString("F3", CultureInfo.InvariantCulture)} ");

                // フィルターがある場合は追加
                if (filters.Count > 0)
                {
                    var filterString = string.Join(",", filters);
                    arguments.Append($"-vf \"{filterString}\" ");
                }

                // 出力設定
                arguments.Append("-c:v libx264 ");
                arguments.Append("-preset fast ");
                arguments.Append("-crf 18 ");
                arguments.Append("-c:a aac ");
                arguments.Append("-b:a 192k ");
                arguments.Append($"-r {config.FrameRate} ");
                
                // 出力ファイル
                arguments.Append($"\"{outputPath}\"");

                LogHelper.WriteLog(
                    "VideoExporter:ProcessSegmentWithXabeAsync",
                    "Starting FFmpeg conversion",
                    new { 
                        segmentId = segment.Id, 
                        startTime = segment.StartTime, 
                        duration = segment.Duration,
                        filterCount = filters.Count,
                        outputPath = outputPath,
                        arguments = arguments.ToString()
                    });

                // FFmpegを直接実行
                var ffmpegPath = FFmpegManager.GetFFmpegExecutablePath();
                var result = await RunFFmpegProcessAsync(ffmpegPath, arguments.ToString(), cancellationToken);

                if (result)
                {
                    LogHelper.WriteLog(
                        "VideoExporter:ProcessSegmentWithXabeAsync",
                        "Segment processed",
                        new { segmentId = segment.Id, outputPath = outputPath });
                }

                return result && File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "VideoExporter:ProcessSegmentWithXabeAsync",
                    "Error processing segment",
                    new { segmentId = segment.Id, error = ex.Message, stackTrace = ex.StackTrace });
                return false;
            }
        }

        /// <summary>
        /// FFmpegプロセスを直接実行
        /// </summary>
        private async Task<bool> RunFFmpegProcessAsync(string ffmpegPath, string arguments, CancellationToken cancellationToken)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                var errorOutput = new StringBuilder();
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginErrorReadLine();

                // キャンセルを監視しながら待機
                using (cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch { }
                }))
                {
                    await process.WaitForExitAsync(cancellationToken);
                }

                if (process.ExitCode != 0)
                {
                    LogHelper.WriteLog(
                        "VideoExporter:RunFFmpegProcessAsync",
                        "FFmpeg exited with error",
                        new { exitCode = process.ExitCode, error = errorOutput.ToString() });
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "VideoExporter:RunFFmpegProcessAsync",
                    "Error running FFmpeg",
                    new { error = ex.Message });
                return false;
            }
        }

        /// <summary>
        /// テロップ用のフィルターを追加
        /// </summary>
        private void AddTextOverlayFilters(List<string> filters, SceneBlock sceneBlock, ProjectConfig config)
        {
            LogHelper.WriteLog(
                "VideoExporter:AddTextOverlayFilters",
                "Adding text overlays",
                new { 
                    sceneId = sceneBlock.Id,
                    title = sceneBlock.Title,
                    subtitle = sceneBlock.Subtitle,
                    freeTextCount = sceneBlock.FreeTextItems?.Count ?? 0
                });

            // タイトル（# で始まる行）
            if (!string.IsNullOrWhiteSpace(sceneBlock.Title))
            {
                var titleText = sceneBlock.Title.TrimStart('#').Trim();
                var titleFilter = BuildDrawTextFilter(
                    titleText,
                    config.TitlePositionX,
                    config.TitlePositionY,
                    config.TitleFontSize,
                    config.DefaultTitleColor,
                    config.DefaultFont,
                    config.DefaultBackgroundAlpha);
                filters.Add(titleFilter);
                
                LogHelper.WriteLog(
                    "VideoExporter:AddTextOverlayFilters",
                    "Added title filter",
                    new { titleText = titleText, filter = titleFilter });
            }

            // 字幕（> で始まる行）
            if (!string.IsNullOrWhiteSpace(sceneBlock.Subtitle))
            {
                var subtitleText = sceneBlock.Subtitle.TrimStart('>').Trim();
                var subtitleFilter = BuildDrawTextFilter(
                    subtitleText,
                    0.5, // X位置は中央
                    config.SubtitlePositionY,
                    config.SubtitleFontSize,
                    config.DefaultSubtitleColor,
                    config.DefaultFont,
                    config.DefaultBackgroundAlpha,
                    isCentered: true);
                filters.Add(subtitleFilter);
                
                LogHelper.WriteLog(
                    "VideoExporter:AddTextOverlayFilters",
                    "Added subtitle filter",
                    new { subtitleText = subtitleText, filter = subtitleFilter });
            }

            // 自由テキスト
            if (sceneBlock.FreeTextItems != null && sceneBlock.FreeTextItems.Count > 0)
            {
                foreach (var freeText in sceneBlock.FreeTextItems)
                {
                    var freeTextFilter = BuildDrawTextFilter(
                        freeText.Text,
                        freeText.X,
                        freeText.Y,
                        freeText.FontSize ?? config.DefaultFontSize,
                        string.IsNullOrEmpty(freeText.TextColor) ? config.DefaultFreeTextColor : freeText.TextColor,
                        config.DefaultFont,
                        config.DefaultBackgroundAlpha);
                    filters.Add(freeTextFilter);
                    
                    LogHelper.WriteLog(
                        "VideoExporter:AddTextOverlayFilters",
                        "Added free text filter",
                        new { text = freeText.Text, filter = freeTextFilter });
                }
            }

            LogHelper.WriteLog(
                "VideoExporter:AddTextOverlayFilters",
                "Text overlay filters completed",
                new { totalFilterCount = filters.Count });
        }

        /// <summary>
        /// drawtext フィルターを構築
        /// </summary>
        private string BuildDrawTextFilter(
            string text,
            double posX,
            double posY,
            int fontSize,
            string fontColor,
            string fontFamily,
            double backgroundAlpha,
            bool isCentered = false)
        {
            // テキストをエスケープ
            var escapedText = text
                .Replace("\\", "\\\\")
                .Replace(":", "\\:")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "");

            // 色をFFmpeg形式に変換（#RRGGBBからRRGGBBへ）
            var color = fontColor.TrimStart('#');

            // 背景のアルファ値を計算
            var bgAlpha = (int)(backgroundAlpha * 255);
            var bgColor = $"000000@0x{bgAlpha:X2}";

            // 位置の計算
            string xPosition;
            if (isCentered)
            {
                xPosition = "(w-text_w)/2"; // 中央揃え
            }
            else
            {
                xPosition = $"w*{posX.ToString("F3", CultureInfo.InvariantCulture)}";
            }
            var yPosition = $"h*{posY.ToString("F3", CultureInfo.InvariantCulture)}";

            // フォントファイルの指定（Windowsの場合）
            var fontFile = GetFontFilePath(fontFamily);

            var filter = new StringBuilder();
            filter.Append($"drawtext=text='{escapedText}'");
            
            if (!string.IsNullOrEmpty(fontFile))
            {
                filter.Append($":fontfile='{fontFile}'");
            }
            
            filter.Append($":fontsize={fontSize}");
            filter.Append($":fontcolor={color}");
            filter.Append($":x={xPosition}");
            filter.Append($":y={yPosition}");
            filter.Append($":box=1");
            filter.Append($":boxcolor={bgColor}");
            filter.Append($":boxborderw=5");

            return filter.ToString();
        }

        /// <summary>
        /// フォント名からフォントファイルパスを取得
        /// </summary>
        private string GetFontFilePath(string fontFamily)
        {
            // Windowsのフォントディレクトリ
            var fontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));

            // 一般的なフォント名とファイル名のマッピング
            var fontMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "メイリオ", "meiryo.ttc" },
                { "Meiryo", "meiryo.ttc" },
                { "Yu Gothic", "YuGothM.ttc" },
                { "游ゴシック", "YuGothM.ttc" },
                { "MS Gothic", "msgothic.ttc" },
                { "ＭＳ ゴシック", "msgothic.ttc" },
                { "MS PGothic", "msgothic.ttc" },
                { "ＭＳ Ｐゴシック", "msgothic.ttc" },
                { "MS Mincho", "msmincho.ttc" },
                { "ＭＳ 明朝", "msmincho.ttc" },
                { "Arial", "arial.ttf" },
                { "Times New Roman", "times.ttf" },
                { "Segoe UI", "segoeui.ttf" }
            };

            if (fontMapping.TryGetValue(fontFamily, out var fontFile))
            {
                var fontPath = Path.Combine(fontsDir, fontFile);
                if (File.Exists(fontPath))
                {
                    return fontPath.Replace("\\", "/").Replace(":", "\\:");
                }
            }

            // デフォルトでメイリオを使用
            var defaultFont = Path.Combine(fontsDir, "meiryo.ttc");
            if (File.Exists(defaultFont))
            {
                return defaultFont.Replace("\\", "/").Replace(":", "\\:");
            }

            return string.Empty;
        }

        /// <summary>
        /// 動画を結合
        /// </summary>
        private async Task<bool> ConcatenateVideosWithXabeAsync(
            List<string> inputFiles,
            string outputPath,
            ProjectConfig config,
            CancellationToken cancellationToken)
        {
            try
            {
                if (inputFiles.Count == 0)
                {
                    return false;
                }

                // ファイルが1つだけの場合はコピー
                if (inputFiles.Count == 1)
                {
                    File.Copy(inputFiles[0], outputPath, true);
                    return true;
                }

                // concat demuxerを使用するためのリストファイルを作成
                var tempDir = Path.GetDirectoryName(inputFiles[0])!;
                var concatListFile = Path.Combine(tempDir, "concat_list.txt");

                var concatListContent = new StringBuilder();
                foreach (var file in inputFiles)
                {
                    // ファイルパスをエスケープ
                    var escapedPath = file.Replace("\\", "/").Replace("'", "'\\''");
                    concatListContent.AppendLine($"file '{escapedPath}'");
                }
                await File.WriteAllTextAsync(concatListFile, concatListContent.ToString(), cancellationToken);

                // FFmpegコマンドを構築
                var arguments = $"-y -f concat -safe 0 -i \"{concatListFile}\" -c copy \"{outputPath}\"";

                // FFmpegを直接実行
                var ffmpegPath = FFmpegManager.GetFFmpegExecutablePath();
                var result = await RunFFmpegProcessAsync(ffmpegPath, arguments, cancellationToken);

                if (result)
                {
                    LogHelper.WriteLog(
                        "VideoExporter:ConcatenateVideosWithXabeAsync",
                        "Videos concatenated",
                        new { outputPath = outputPath, inputCount = inputFiles.Count });
                }

                return result && File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "VideoExporter:ConcatenateVideosWithXabeAsync",
                    "Error concatenating videos",
                    new { error = ex.Message, stackTrace = ex.StackTrace });
                return false;
            }
        }
    }
}
