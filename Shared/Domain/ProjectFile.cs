using System;
using System.Collections.Generic;

namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// プロジェクトファイルのデータ構造を表すクラス。
    /// JSONシリアライゼーション用のDTO（Data Transfer Object）として使用します。
    /// </summary>
    public class ProjectFile
    {
        /// <summary>
        /// プロジェクトファイルのバージョン
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// プロジェクトの作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// プロジェクトの最終更新日時
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// プロジェクトの名前
        /// </summary>
        public string? ProjectName { get; set; }

        /// <summary>
        /// プロジェクトの説明
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// シーンブロックのリスト
        /// </summary>
        public List<SceneBlock> SceneBlocks { get; set; } = new();

        /// <summary>
        /// プロジェクトの総時間（秒）
        /// </summary>
        public double TotalDuration { get; set; }

        /// <summary>
        /// メタデータ（キー・バリューペア）
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();

        /// <summary>
        /// 動画ファイルのパス
        /// </summary>
        public string? VideoFilePath { get; set; }

        /// <summary>
        /// サムネイルデータ
        /// </summary>
        public ThumbnailData? ThumbnailData { get; set; }
    }
}
