using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// プロジェクトファイル（.wideor）の読み書きを行うサービスの実装
    /// テキストベースのHeader/Body形式を処理します。
    /// </summary>
    public class ProjectFileService : IProjectFileService
    {
        private const string HeaderSeparator = "===";
        private const string ProjectFileExtension = ".wideor";

        // Header コマンドの正規表現パターン
        private static readonly Regex ProjectPattern = new(@"^PROJECT\s+""([^""]+)""", RegexOptions.IgnoreCase);
        private static readonly Regex ResolutionPattern = new(@"^RESOLUTION\s+(\d+)x(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex FrameRatePattern = new(@"^FRAMERATE\s+(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex DefaultFontPattern = new(@"^DEFAULT_FONT\s+""([^""]+)""", RegexOptions.IgnoreCase);
        private static readonly Regex DefaultFontSizePattern = new(@"^DEFAULT_FONT_SIZE\s+(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex DefaultTitleColorPattern = new(@"^DEFAULT_TITLE_COLOR\s+(#[0-9A-Fa-f]{6,8})", RegexOptions.IgnoreCase);
        private static readonly Regex DefaultSubtitleColorPattern = new(@"^DEFAULT_SUBTITLE_COLOR\s+(#[0-9A-Fa-f]{6,8})", RegexOptions.IgnoreCase);
        private static readonly Regex DefaultBackgroundAlphaPattern = new(@"^DEFAULT_BACKGROUND_ALPHA\s+([\d.]+)", RegexOptions.IgnoreCase);
        private static readonly Regex TitlePositionXPattern = new(@"^TITLE_POSITION_X\s+([\d.]+)", RegexOptions.IgnoreCase);
        private static readonly Regex TitlePositionYPattern = new(@"^TITLE_POSITION_Y\s+([\d.]+)", RegexOptions.IgnoreCase);
        private static readonly Regex SubtitlePositionYPattern = new(@"^SUBTITLE_POSITION_Y\s+([\d.]+)", RegexOptions.IgnoreCase);
        private static readonly Regex TitleFontSizePattern = new(@"^TITLE_FONT_SIZE\s+(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex SubtitleFontSizePattern = new(@"^SUBTITLE_FONT_SIZE\s+(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex DefaultFreeTextColorPattern = new(@"^DEFAULT_FREETEXT_COLOR\s+(#[0-9A-Fa-f]{6,8})", RegexOptions.IgnoreCase);

        // Body コマンドの正規表現パターン
        private static readonly Regex LoadPattern = new(@"^LOAD\s+(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase);

        /// <summary>
        /// プロジェクトファイルを読み込みます。
        /// </summary>
        public async Task<ProjectFileData?> LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    LogHelper.WriteLog(
                        "ProjectFileService:LoadAsync",
                        "File path is null or empty",
                        null);
                    return null;
                }

                if (!File.Exists(filePath))
                {
                    LogHelper.WriteLog(
                        "ProjectFileService:LoadAsync",
                        "File not found",
                        new { filePath = filePath });
                    return null;
                }

                var textContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
                var data = ParseText(textContent);
                data.FilePath = filePath;

                // 相対パスの動画ファイルを絶対パスに変換
                if (!string.IsNullOrEmpty(data.VideoFilePath) && !Path.IsPathRooted(data.VideoFilePath))
                {
                    var projectDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(projectDir))
                    {
                        data.VideoFilePath = Path.GetFullPath(Path.Combine(projectDir, data.VideoFilePath));
                    }
                }

                LogHelper.WriteLog(
                    "ProjectFileService:LoadAsync",
                    "Project file loaded successfully",
                    new { filePath = filePath, videoFilePath = data.VideoFilePath });

                return data;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "ProjectFileService:LoadAsync",
                    "Failed to load project file",
                    new { filePath = filePath, error = ex.Message });
                return null;
            }
        }

        /// <summary>
        /// プロジェクトファイルを保存します。
        /// </summary>
        public async Task<bool> SaveAsync(string filePath, ProjectFileData data, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    LogHelper.WriteLog(
                        "ProjectFileService:SaveAsync",
                        "File path is null or empty",
                        null);
                    return false;
                }

                // 拡張子を追加（ない場合）
                if (!Path.GetExtension(filePath).Equals(ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    filePath = Path.ChangeExtension(filePath, ProjectFileExtension);
                }

                // ディレクトリが存在しない場合は作成
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // テキスト内容を生成
                var textContent = string.IsNullOrEmpty(data.TextContent) 
                    ? GenerateText(data) 
                    : data.TextContent;

                await File.WriteAllTextAsync(filePath, textContent, Encoding.UTF8, cancellationToken);

                LogHelper.WriteLog(
                    "ProjectFileService:SaveAsync",
                    "Project file saved successfully",
                    new { filePath = filePath });

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "ProjectFileService:SaveAsync",
                    "Failed to save project file",
                    new { filePath = filePath, error = ex.Message });
                return false;
            }
        }

        /// <summary>
        /// テキスト内容からプロジェクトデータを生成します。
        /// </summary>
        public ProjectFileData ParseText(string textContent)
        {
            var data = new ProjectFileData
            {
                TextContent = textContent,
                Config = new ProjectConfig()
            };

            if (string.IsNullOrWhiteSpace(textContent))
            {
                return data;
            }

            var lines = textContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var isInHeader = true;
            var bodyStartLine = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Header区切りを検出
                if (line.StartsWith("==="))
                {
                    isInHeader = false;
                    bodyStartLine = i + 1;
                    continue;
                }

                if (isInHeader)
                {
                    ParseHeaderLine(line, data.Config);
                }
                else
                {
                    // LOADコマンドを検索
                    var loadMatch = LoadPattern.Match(line);
                    if (loadMatch.Success)
                    {
                        data.VideoFilePath = loadMatch.Groups[1].Success 
                            ? loadMatch.Groups[1].Value 
                            : loadMatch.Groups[2].Value;
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Header行をパースしてProjectConfigに反映
        /// </summary>
        private void ParseHeaderLine(string line, ProjectConfig config)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
            {
                return; // コメント行はスキップ
            }

            Match match;

            if ((match = ProjectPattern.Match(line)).Success)
            {
                config.ProjectName = match.Groups[1].Value;
            }
            else if ((match = ResolutionPattern.Match(line)).Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int width) &&
                    int.TryParse(match.Groups[2].Value, out int height))
                {
                    config.ResolutionWidth = width;
                    config.ResolutionHeight = height;
                }
            }
            else if ((match = FrameRatePattern.Match(line)).Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int frameRate))
                {
                    config.FrameRate = frameRate;
                }
            }
            else if ((match = DefaultFontPattern.Match(line)).Success)
            {
                config.DefaultFont = match.Groups[1].Value;
            }
            else if ((match = DefaultFontSizePattern.Match(line)).Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int fontSize))
                {
                    config.DefaultFontSize = fontSize;
                }
            }
            else if ((match = DefaultTitleColorPattern.Match(line)).Success)
            {
                config.DefaultTitleColor = match.Groups[1].Value;
            }
            else if ((match = DefaultSubtitleColorPattern.Match(line)).Success)
            {
                config.DefaultSubtitleColor = match.Groups[1].Value;
            }
            else if ((match = DefaultBackgroundAlphaPattern.Match(line)).Success)
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha))
                {
                    config.DefaultBackgroundAlpha = alpha;
                }
            }
            else if ((match = TitlePositionXPattern.Match(line)).Success)
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double posX))
                {
                    config.TitlePositionX = posX;
                }
            }
            else if ((match = TitlePositionYPattern.Match(line)).Success)
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double posY))
                {
                    config.TitlePositionY = posY;
                }
            }
            else if ((match = SubtitlePositionYPattern.Match(line)).Success)
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double posY))
                {
                    config.SubtitlePositionY = posY;
                }
            }
            else if ((match = TitleFontSizePattern.Match(line)).Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int fontSize))
                {
                    config.TitleFontSize = fontSize;
                }
            }
            else if ((match = SubtitleFontSizePattern.Match(line)).Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int fontSize))
                {
                    config.SubtitleFontSize = fontSize;
                }
            }
            else if ((match = DefaultFreeTextColorPattern.Match(line)).Success)
            {
                config.DefaultFreeTextColor = match.Groups[1].Value;
            }
        }

        /// <summary>
        /// プロジェクトデータからテキスト内容を生成します。
        /// </summary>
        public string GenerateText(ProjectFileData data)
        {
            var sb = new StringBuilder();
            var config = data.Config;

            // Header セクション
            sb.AppendLine($"PROJECT \"{config.ProjectName}\"");
            sb.AppendLine($"RESOLUTION {config.ResolutionWidth}x{config.ResolutionHeight}");
            sb.AppendLine($"FRAMERATE {config.FrameRate}");
            sb.AppendLine($"DEFAULT_FONT \"{config.DefaultFont}\"");
            sb.AppendLine($"DEFAULT_FONT_SIZE {config.DefaultFontSize}");
            sb.AppendLine($"DEFAULT_TITLE_COLOR {config.DefaultTitleColor}");
            sb.AppendLine($"DEFAULT_SUBTITLE_COLOR {config.DefaultSubtitleColor}");
            sb.AppendLine($"DEFAULT_BACKGROUND_ALPHA {config.DefaultBackgroundAlpha.ToString("F1", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"TITLE_POSITION_X {config.TitlePositionX.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"TITLE_POSITION_Y {config.TitlePositionY.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"SUBTITLE_POSITION_Y {config.SubtitlePositionY.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"TITLE_FONT_SIZE {config.TitleFontSize}");
            sb.AppendLine($"SUBTITLE_FONT_SIZE {config.SubtitleFontSize}");
            sb.AppendLine($"DEFAULT_FREETEXT_COLOR {config.DefaultFreeTextColor}");
            sb.AppendLine(HeaderSeparator);
            sb.AppendLine();

            // Body セクション
            if (!string.IsNullOrEmpty(data.VideoFilePath))
            {
                // パスにスペースが含まれている場合はクォートで囲む
                if (data.VideoFilePath.Contains(" "))
                {
                    sb.AppendLine($"LOAD \"{data.VideoFilePath}\"");
                }
                else
                {
                    sb.AppendLine($"LOAD {data.VideoFilePath}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
