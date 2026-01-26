using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// シーンパーサーの実装
    /// テキストからパラグラフ（セパレータ形式）をパースしてSceneBlockのリストを生成します。
    /// </summary>
    public class SceneParser : ISceneParser
    {
        // セパレータ形式の正規表現
        // 形式: --- [00:01:15.000 -> 00:01:20.500] ---
        private static readonly Regex SeparatorPattern = new Regex(
            @"^[-]{3,}\s*\[(?<startHour>\d{2}):(?<startMin>\d{2}):(?<startSec>\d{2})\.(?<startMs>\d{3})\s*->\s*(?<endHour>\d{2}):(?<endMin>\d{2}):(?<endSec>\d{2})\.(?<endMs>\d{3})\]\s*[-]{3,}$",
            RegexOptions.Compiled);

        // コマンド行の判定用正規表現
        private static readonly Regex CommandLinePattern = new Regex(
            @"^\s*(LOAD|CUT|HIDE|SHOW|DELETE)\s+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// テキストからシーンブロックのリストをパースします。
        /// </summary>
        /// <param name="text">パース対象のテキスト（Body部分）</param>
        /// <returns>SceneBlockのリスト</returns>
        public List<SceneBlock> ParseScenes(string text)
        {
            var scenes = new List<SceneBlock>();

            if (string.IsNullOrEmpty(text))
                return scenes;

            // 改行で分割（\r\n, \n の両方に対応）
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // 各行を処理
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var match = SeparatorPattern.Match(line);

                if (match.Success)
                {
                    // セパレータ行を発見
                    var scene = ParseSceneBlock(lines, i, match);
                    if (scene != null)
                    {
                        scenes.Add(scene);
                    }
                }
            }

            LogHelper.WriteLog(
                "SceneParser:ParseScenes",
                "Scenes parsed",
                new { sceneCount = scenes.Count });

            return scenes;
        }

        /// <summary>
        /// セパレータ行からSceneBlockを生成します。
        /// </summary>
        private SceneBlock? ParseSceneBlock(string[] lines, int separatorIndex, Match match)
        {
            try
            {
                // StartTime/EndTime を抽出
                var startTime = ParseTimeFromMatch(
                    match.Groups["startHour"].Value,
                    match.Groups["startMin"].Value,
                    match.Groups["startSec"].Value,
                    match.Groups["startMs"].Value);

                var endTime = ParseTimeFromMatch(
                    match.Groups["endHour"].Value,
                    match.Groups["endMin"].Value,
                    match.Groups["endSec"].Value,
                    match.Groups["endMs"].Value);

                // 次のセパレータまでのコンテンツを収集
                var contentLines = new List<string>();
                int contentStartIndex = separatorIndex + 1;

                for (int j = contentStartIndex; j < lines.Length; j++)
                {
                    // 次のセパレータに到達したら終了
                    if (SeparatorPattern.IsMatch(lines[j]))
                        break;

                    // コマンド行（LOAD, CUT等）に到達したら終了
                    if (IsCommandLine(lines[j]))
                        break;

                    contentLines.Add(lines[j]);
                }

                // コンテンツからタイトル、字幕を抽出
                var contentText = string.Join("\n", contentLines).Trim();
                var (title, subtitle) = ExtractTitleAndSubtitle(contentText);

                // SceneBlock を生成
                var scene = new SceneBlock
                {
                    Id = Guid.NewGuid().ToString(),
                    StartTime = startTime,
                    EndTime = endTime,
                    LineNumber = separatorIndex + 1, // 1ベースの行番号
                    ContentText = contentText,
                    Title = title,
                    Subtitle = subtitle
                };

                return scene;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "SceneParser:ParseSceneBlock",
                    "Failed to parse scene block",
                    new { separatorIndex = separatorIndex, exceptionType = ex.GetType().Name, message = ex.Message });

                return null;
            }
        }

        /// <summary>
        /// 時間、分、秒、ミリ秒からdouble（秒）を生成します。
        /// </summary>
        private double ParseTimeFromMatch(string hour, string minute, string second, string millisecond)
        {
            var h = int.Parse(hour);
            var m = int.Parse(minute);
            var s = int.Parse(second);
            var ms = int.Parse(millisecond);

            return h * 3600 + m * 60 + s + ms / 1000.0;
        }

        /// <summary>
        /// コンテンツからタイトル（# で始まる行）と字幕（> で始まる行）を抽出します。
        /// </summary>
        private (string? title, string? subtitle) ExtractTitleAndSubtitle(string contentText)
        {
            string? title = null;
            string? subtitle = null;

            var lines = contentText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                var trimmedLine = line.TrimStart();

                if (trimmedLine.StartsWith("# ") && title == null)
                {
                    title = trimmedLine.Substring(2).Trim();
                }
                else if (trimmedLine.StartsWith("> ") && subtitle == null)
                {
                    subtitle = trimmedLine.Substring(2).Trim();
                }
            }

            return (title, subtitle);
        }

        /// <summary>
        /// 行がコマンド行かどうかを判定します。
        /// </summary>
        private bool IsCommandLine(string line)
        {
            var trimmed = line.Trim();
            return CommandLinePattern.IsMatch(trimmed);
        }

        /// <summary>
        /// SceneBlockからセパレータ形式のテキストを生成します。
        /// </summary>
        /// <param name="scene">シーンブロック</param>
        /// <returns>セパレータ形式のテキスト</returns>
        public string GenerateSceneText(SceneBlock scene)
        {
            var sb = new StringBuilder();

            // セパレータ行を生成
            var startTs = TimeSpan.FromSeconds(scene.StartTime);
            var endTs = TimeSpan.FromSeconds(scene.EndTime);

            var startTimeStr = $"{startTs.Hours:D2}:{startTs.Minutes:D2}:{startTs.Seconds:D2}.{startTs.Milliseconds:D3}";
            var endTimeStr = $"{endTs.Hours:D2}:{endTs.Minutes:D2}:{endTs.Seconds:D2}.{endTs.Milliseconds:D3}";

            sb.AppendLine($"--- [{startTimeStr} -> {endTimeStr}] ---");

            // タイトルがあれば追加
            if (!string.IsNullOrEmpty(scene.Title))
            {
                sb.AppendLine($"# {scene.Title}");
            }

            // 字幕があれば追加
            if (!string.IsNullOrEmpty(scene.Subtitle))
            {
                sb.AppendLine($"> {scene.Subtitle}");
            }

            return sb.ToString();
        }
    }
}
