using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Features.Editor.Internal
{
    /// <summary>
    /// テキストからSceneBlockを生成するパーサー（Editor機能専用のInternalクラス）
    /// </summary>
    internal static class SceneParser
    {
        // 時間コマンドの正規表現パターン（例: [00:01:23-00:01:45] または [01:23-01:45]）
        private static readonly Regex TimeCommandRegex = new(
            @"\[(\d{1,2}):(\d{2}):(\d{2})-(\d{1,2}):(\d{2}):(\d{2})\]|\[(\d{1,2}):(\d{2})-(\d{1,2}):(\d{2})\]",
            RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>
        /// テキスト全文を解析し、不変のシーンリストを生成する純粋関数。
        /// </summary>
        /// <param name="fullText">解析するテキスト</param>
        /// <returns>シーンブロックのリスト</returns>
        public static IReadOnlyList<SceneBlock> Parse(string fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText))
            {
                return Array.Empty<SceneBlock>();
            }

            var scenes = new List<SceneBlock>();
            var lines = fullText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            SceneBlock? currentScene = null;
            var contentBuilder = new System.Text.StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedLine = line.Trim();

                if (IsCommandLine(trimmedLine))
                {
                    // 前のシーンを確定
                    if (currentScene != null)
                    {
                        var finalScene = currentScene with { Title = contentBuilder.ToString().Trim() };
                        scenes.Add(finalScene);
                        contentBuilder.Clear();
                    }

                    // 新しいシーンを開始
                    var timeRange = ExtractTimeRange(trimmedLine);
                    if (timeRange.HasValue)
                    {
                        currentScene = new SceneBlock
                        {
                            Id = Guid.NewGuid().ToString(),
                            StartTime = timeRange.Value.Start,
                            EndTime = timeRange.Value.End,
                            Title = string.Empty
                        };
                    }
                }
                else if (currentScene != null)
                {
                    // コンテンツ行を追加
                    if (contentBuilder.Length > 0)
                    {
                        contentBuilder.AppendLine();
                    }
                    contentBuilder.Append(line);
                }
            }

            // 最後のシーンを確定
            if (currentScene != null)
            {
                var finalScene = currentScene with { Title = contentBuilder.ToString().Trim() };
                scenes.Add(finalScene);
            }

            return scenes;
        }

        /// <summary>
        /// 指定行がコマンド行か判定
        /// </summary>
        public static bool IsCommandLine(string lineText)
        {
            if (string.IsNullOrWhiteSpace(lineText))
                return false;

            return TimeCommandRegex.IsMatch(lineText);
        }

        /// <summary>
        /// 時間範囲から正規化されたコマンド文字列を生成
        /// </summary>
        public static string GenerateCommandText(double startTime, double endTime)
        {
            var startSpan = TimeSpan.FromSeconds(startTime);
            var endSpan = TimeSpan.FromSeconds(endTime);

            // 1時間未満の場合は分:秒形式、1時間以上の場合は時:分:秒形式
            if (startSpan.TotalHours < 1 && endSpan.TotalHours < 1)
            {
                return $"[{startSpan.Minutes:D2}:{startSpan.Seconds:D2}-{endSpan.Minutes:D2}:{endSpan.Seconds:D2}]";
            }
            else
            {
                return $"[{(int)startSpan.TotalHours:D2}:{startSpan.Minutes:D2}:{startSpan.Seconds:D2}-{(int)endSpan.TotalHours:D2}:{endSpan.Minutes:D2}:{endSpan.Seconds:D2}]";
            }
        }

        /// <summary>
        /// コマンド行から時間範囲を抽出
        /// </summary>
        private static (double Start, double End)? ExtractTimeRange(string commandLine)
        {
            var match = TimeCommandRegex.Match(commandLine);
            if (!match.Success)
                return null;

            try
            {
                double startSeconds, endSeconds;

                if (match.Groups[1].Success)
                {
                    // 時:分:秒形式 [HH:MM:SS-HH:MM:SS]
                    var startHours = int.Parse(match.Groups[1].Value);
                    var startMinutes = int.Parse(match.Groups[2].Value);
                    var startSecs = int.Parse(match.Groups[3].Value);
                    var endHours = int.Parse(match.Groups[4].Value);
                    var endMinutes = int.Parse(match.Groups[5].Value);
                    var endSecs = int.Parse(match.Groups[6].Value);

                    startSeconds = startHours * 3600 + startMinutes * 60 + startSecs;
                    endSeconds = endHours * 3600 + endMinutes * 60 + endSecs;
                }
                else
                {
                    // 分:秒形式 [MM:SS-MM:SS]
                    var startMinutes = int.Parse(match.Groups[7].Value);
                    var startSecs = int.Parse(match.Groups[8].Value);
                    var endMinutes = int.Parse(match.Groups[9].Value);
                    var endSecs = int.Parse(match.Groups[10].Value);

                    startSeconds = startMinutes * 60 + startSecs;
                    endSeconds = endMinutes * 60 + endSecs;
                }

                return (startSeconds, endSeconds);
            }
            catch
            {
                return null;
            }
        }
    }
}
