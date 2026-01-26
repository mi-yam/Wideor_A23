using System;
using System.Text;
using System.Text.RegularExpressions;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// Headerパーサーの実装
    /// テキストのHeaderセクションをパースしてProjectConfigを生成します。
    /// </summary>
    public class HeaderParser : IHeaderParser
    {
        // Header コマンドの正規表現パターン
        private static readonly Regex ProjectPattern = new Regex(
            @"^\s*PROJECT\s+""(.+)""$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ResolutionPattern = new Regex(
            @"^\s*RESOLUTION\s+(\d+)x(\d+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FrameRatePattern = new Regex(
            @"^\s*FRAMERATE\s+(\d+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DefaultFontPattern = new Regex(
            @"^\s*DEFAULT_FONT\s+""(.+)""$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DefaultFontSizePattern = new Regex(
            @"^\s*DEFAULT_FONT_SIZE\s+(\d+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DefaultTitleColorPattern = new Regex(
            @"^\s*DEFAULT_TITLE_COLOR\s+#([0-9A-Fa-f]{6})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DefaultSubtitleColorPattern = new Regex(
            @"^\s*DEFAULT_SUBTITLE_COLOR\s+#([0-9A-Fa-f]{6})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DefaultBackgroundAlphaPattern = new Regex(
            @"^\s*DEFAULT_BACKGROUND_ALPHA\s+(0?\.\d+|1\.0|0|1)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Header と Body の区切り（=== で3つ以上）
        private static readonly Regex SeparatorPattern = new Regex(
            @"^={3,}$",
            RegexOptions.Compiled);

        /// <summary>
        /// テキストからHeaderをパースしてProjectConfigを生成します。
        /// </summary>
        /// <param name="text">パース対象のテキスト全体</param>
        /// <returns>ProjectConfigとBody開始行番号のタプル</returns>
        public (ProjectConfig config, int bodyStartLine) ParseHeader(string text)
        {
            var config = new ProjectConfig();

            if (string.IsNullOrEmpty(text))
            {
                return (config, 0);
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int bodyStartLine = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // 区切り文字を検出（=== で Header と Body を分離）
                if (SeparatorPattern.IsMatch(line))
                {
                    bodyStartLine = i + 1;
                    LogHelper.WriteLog(
                        "HeaderParser:ParseHeader",
                        "Header/Body separator found",
                        new { separatorLine = i, bodyStartLine = bodyStartLine });
                    break;
                }

                // 各 Header コマンドをパース
                ParseHeaderCommand(line, config);
            }

            LogHelper.WriteLog(
                "HeaderParser:ParseHeader",
                "Header parsing completed",
                new
                {
                    projectName = config.ProjectName,
                    resolution = config.Resolution,
                    frameRate = config.FrameRate,
                    bodyStartLine = bodyStartLine
                });

            return (config, bodyStartLine);
        }

        /// <summary>
        /// 1行のHeaderコマンドをパースしてProjectConfigに反映します。
        /// </summary>
        private void ParseHeaderCommand(string line, ProjectConfig config)
        {
            // 空行やコメント行はスキップ
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
            {
                return;
            }

            // PROJECT コマンド
            var projectMatch = ProjectPattern.Match(line);
            if (projectMatch.Success)
            {
                config.ProjectName = projectMatch.Groups[1].Value;
                return;
            }

            // RESOLUTION コマンド
            var resolutionMatch = ResolutionPattern.Match(line);
            if (resolutionMatch.Success)
            {
                if (int.TryParse(resolutionMatch.Groups[1].Value, out int width) &&
                    int.TryParse(resolutionMatch.Groups[2].Value, out int height))
                {
                    config.ResolutionWidth = width;
                    config.ResolutionHeight = height;
                }
                return;
            }

            // FRAMERATE コマンド
            var frameRateMatch = FrameRatePattern.Match(line);
            if (frameRateMatch.Success)
            {
                if (int.TryParse(frameRateMatch.Groups[1].Value, out int fps))
                {
                    config.FrameRate = fps;
                }
                return;
            }

            // DEFAULT_FONT コマンド
            var defaultFontMatch = DefaultFontPattern.Match(line);
            if (defaultFontMatch.Success)
            {
                config.DefaultFont = defaultFontMatch.Groups[1].Value;
                return;
            }

            // DEFAULT_FONT_SIZE コマンド
            var defaultFontSizeMatch = DefaultFontSizePattern.Match(line);
            if (defaultFontSizeMatch.Success)
            {
                if (int.TryParse(defaultFontSizeMatch.Groups[1].Value, out int fontSize))
                {
                    config.DefaultFontSize = fontSize;
                }
                return;
            }

            // DEFAULT_TITLE_COLOR コマンド
            var defaultTitleColorMatch = DefaultTitleColorPattern.Match(line);
            if (defaultTitleColorMatch.Success)
            {
                config.DefaultTitleColor = "#" + defaultTitleColorMatch.Groups[1].Value.ToUpper();
                return;
            }

            // DEFAULT_SUBTITLE_COLOR コマンド
            var defaultSubtitleColorMatch = DefaultSubtitleColorPattern.Match(line);
            if (defaultSubtitleColorMatch.Success)
            {
                config.DefaultSubtitleColor = "#" + defaultSubtitleColorMatch.Groups[1].Value.ToUpper();
                return;
            }

            // DEFAULT_BACKGROUND_ALPHA コマンド
            var defaultBackgroundAlphaMatch = DefaultBackgroundAlphaPattern.Match(line);
            if (defaultBackgroundAlphaMatch.Success)
            {
                if (double.TryParse(defaultBackgroundAlphaMatch.Groups[1].Value, out double alpha))
                {
                    config.DefaultBackgroundAlpha = alpha;
                }
                return;
            }
        }

        /// <summary>
        /// ProjectConfigからHeaderテキストを生成します。
        /// </summary>
        /// <param name="config">プロジェクト設定</param>
        /// <returns>Header形式のテキスト</returns>
        public string GenerateHeaderText(ProjectConfig config)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"PROJECT \"{config.ProjectName}\"");
            sb.AppendLine($"RESOLUTION {config.ResolutionWidth}x{config.ResolutionHeight}");
            sb.AppendLine($"FRAMERATE {config.FrameRate}");
            sb.AppendLine($"DEFAULT_FONT \"{config.DefaultFont}\"");
            sb.AppendLine($"DEFAULT_FONT_SIZE {config.DefaultFontSize}");
            sb.AppendLine($"DEFAULT_TITLE_COLOR {config.DefaultTitleColor}");
            sb.AppendLine($"DEFAULT_SUBTITLE_COLOR {config.DefaultSubtitleColor}");
            sb.AppendLine($"DEFAULT_BACKGROUND_ALPHA {config.DefaultBackgroundAlpha:F1}");
            sb.AppendLine("===");

            return sb.ToString();
        }
    }
}
