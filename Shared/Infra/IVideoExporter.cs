using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// 動画書き出し機能のインターフェース
    /// FFmpegを使用して、テキストエリアの処理に基づきフィルムエリアを順番に再生した動画を生成します。
    /// </summary>
    public interface IVideoExporter
    {
        /// <summary>
        /// 動画を書き出します。
        /// </summary>
        /// <param name="segments">書き出すセグメントのリスト（表示順）</param>
        /// <param name="sceneBlocks">テロップ情報を含むシーンブロックのリスト</param>
        /// <param name="config">プロジェクト設定</param>
        /// <param name="outputPath">出力ファイルパス</param>
        /// <param name="progress">進捗報告用のコールバック</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>成功した場合true</returns>
        Task<bool> ExportAsync(
            IReadOnlyList<VideoSegment> segments,
            IReadOnlyList<SceneBlock> sceneBlocks,
            ProjectConfig config,
            string outputPath,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// FFmpegが利用可能かどうかを確認します。
        /// </summary>
        /// <returns>FFmpegが利用可能な場合true</returns>
        Task<bool> IsFFmpegAvailableAsync();

        /// <summary>
        /// 書き出しがキャンセル可能かどうか
        /// </summary>
        bool CanCancel { get; }
    }

    /// <summary>
    /// エクスポート進捗情報
    /// </summary>
    public class ExportProgress
    {
        /// <summary>
        /// 進捗率（0.0～1.0）
        /// </summary>
        public double Progress { get; init; }

        /// <summary>
        /// 現在処理中のステップ
        /// </summary>
        public ExportStep CurrentStep { get; init; }

        /// <summary>
        /// ステータスメッセージ
        /// </summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// 経過時間
        /// </summary>
        public TimeSpan Elapsed { get; init; }

        /// <summary>
        /// 推定残り時間
        /// </summary>
        public TimeSpan? EstimatedRemaining { get; init; }

        /// <summary>
        /// 処理中のセグメント番号（1から開始）
        /// </summary>
        public int CurrentSegmentIndex { get; init; }

        /// <summary>
        /// 総セグメント数
        /// </summary>
        public int TotalSegments { get; init; }
    }

    /// <summary>
    /// エクスポート処理のステップ
    /// </summary>
    public enum ExportStep
    {
        /// <summary>準備中</summary>
        Preparing,
        /// <summary>セグメントを処理中</summary>
        ProcessingSegments,
        /// <summary>テロップを合成中</summary>
        ApplyingOverlays,
        /// <summary>動画を結合中</summary>
        Concatenating,
        /// <summary>エンコード中</summary>
        Encoding,
        /// <summary>完了</summary>
        Completed,
        /// <summary>エラー</summary>
        Error
    }
}
