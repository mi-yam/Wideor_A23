namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// メディア処理中に発生したエラー情報を表す不変データモデル。
    /// </summary>
    public record MediaError
    {
        /// <summary>
        /// エラーの一意な識別子
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// エラーの種類
        /// </summary>
        public required MediaErrorType ErrorType { get; init; }

        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public required string Message { get; init; }

        /// <summary>
        /// エラーが発生したメディアファイルのパス（該当する場合）
        /// </summary>
        public string? MediaFilePath { get; init; }

        /// <summary>
        /// エラーが発生した時間位置（秒、該当する場合）
        /// </summary>
        public double? TimePosition { get; init; }

        /// <summary>
        /// エラーが発生した日時
        /// </summary>
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// 内部例外情報（該当する場合）
        /// </summary>
        public string? InnerException { get; init; }

        /// <summary>
        /// スタックトレース（デバッグ用）
        /// </summary>
        public string? StackTrace { get; init; }

        /// <summary>
        /// エラーの重大度
        /// </summary>
        public MediaErrorSeverity Severity { get; init; } = MediaErrorSeverity.Error;
    }

    /// <summary>
    /// メディアエラーの種類
    /// </summary>
    public enum MediaErrorType
    {
        /// <summary>
        /// 不明なエラー
        /// </summary>
        Unknown,

        /// <summary>
        /// ファイルが見つからない
        /// </summary>
        FileNotFound,

        /// <summary>
        /// ファイル形式がサポートされていない
        /// </summary>
        UnsupportedFormat,

        /// <summary>
        /// ファイルが破損している
        /// </summary>
        CorruptedFile,

        /// <summary>
        /// デコーダーエラー
        /// </summary>
        DecoderError,

        /// <summary>
        /// エンコーダーエラー
        /// </summary>
        EncoderError,

        /// <summary>
        /// メモリ不足
        /// </summary>
        OutOfMemory,

        /// <summary>
        /// アクセス権限エラー
        /// </summary>
        AccessDenied,

        /// <summary>
        /// ネットワークエラー
        /// </summary>
        NetworkError,

        /// <summary>
        /// タイムアウト
        /// </summary>
        Timeout
    }

    /// <summary>
    /// メディアエラーの重大度
    /// </summary>
    public enum MediaErrorSeverity
    {
        /// <summary>
        /// 情報（処理は継続可能）
        /// </summary>
        Information,

        /// <summary>
        /// 警告（処理は継続可能だが注意が必要）
        /// </summary>
        Warning,

        /// <summary>
        /// エラー（処理が中断される可能性がある）
        /// </summary>
        Error,

        /// <summary>
        /// 致命的エラー（処理を継続できない）
        /// </summary>
        Fatal
    }
}
