using System.Collections.Generic;

namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// コマンド実行結果
    /// </summary>
    public class CommandResult
    {
        /// <summary>
        /// 実行が成功したかどうか
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// エラーメッセージ（失敗時）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 実行されたコマンド
        /// </summary>
        public EditCommand? Command { get; set; }

        /// <summary>
        /// 影響を受けたセグメントのID
        /// </summary>
        public List<int> AffectedSegmentIds { get; set; } = new();

        /// <summary>
        /// 成功結果を作成
        /// </summary>
        public static CommandResult Ok(EditCommand command, params int[] affectedIds)
        {
            return new CommandResult
            {
                Success = true,
                Command = command,
                AffectedSegmentIds = new List<int>(affectedIds)
            };
        }

        /// <summary>
        /// 失敗結果を作成
        /// </summary>
        public static CommandResult Fail(EditCommand command, string errorMessage)
        {
            return new CommandResult
            {
                Success = false,
                Command = command,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// 複数コマンドの実行結果
    /// </summary>
    public class CommandExecutionReport
    {
        /// <summary>
        /// 実行されたコマンド数
        /// </summary>
        public int TotalCommands { get; set; }

        /// <summary>
        /// 成功したコマンド数
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失敗したコマンド数
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// 個別の結果
        /// </summary>
        public List<CommandResult> Results { get; set; } = new();

        /// <summary>
        /// すべて成功したかどうか
        /// </summary>
        public bool AllSucceeded => FailureCount == 0;

        /// <summary>
        /// エラーメッセージの一覧
        /// </summary>
        public List<string> ErrorMessages
        {
            get
            {
                var messages = new List<string>();
                foreach (var result in Results)
                {
                    if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        var lineInfo = result.Command != null ? $"行{result.Command.LineNumber}: " : "";
                        messages.Add($"{lineInfo}{result.ErrorMessage}");
                    }
                }
                return messages;
            }
        }
    }
}
