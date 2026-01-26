using System;
using System.Collections.Generic;

namespace Wideor.App.Shared.Domain
{
    /// <summary>
    /// サムネイルデータの構造を表すクラス
    /// </summary>
    public class ThumbnailData
    {
        /// <summary>
        /// 動画ファイルのパス（またはハッシュ）
        /// </summary>
        public string VideoIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// サムネイルの形式（jpeg-tile等）
        /// </summary>
        public string Format { get; set; } = "jpeg-tile";

        /// <summary>
        /// タイルサイズ（例：10x10）
        /// </summary>
        public string TileSize { get; set; } = "10x10";

        /// <summary>
        /// 基本間隔（秒）
        /// </summary>
        public double BaseInterval { get; set; } = 1.0;

        /// <summary>
        /// キーフレーム位置のリスト（秒）
        /// </summary>
        public List<double> KeyFrames { get; set; } = new();

        /// <summary>
        /// マルチスケールのサムネイル情報
        /// </summary>
        public List<ThumbnailScale> Scales { get; set; } = new();

        /// <summary>
        /// サムネイルファイルの保存先ディレクトリ（プロジェクトファイルからの相対パス）
        /// </summary>
        public string? ThumbnailDirectory { get; set; }
    }

    /// <summary>
    /// サムネイルのスケール情報
    /// </summary>
    public class ThumbnailScale
    {
        /// <summary>
        /// サムネイルのサイズ（例：160x90）
        /// </summary>
        public string Size { get; set; } = string.Empty;

        /// <summary>
        /// サムネイルファイルのパス（プロジェクトファイルからの相対パス）
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 幅（ピクセル）
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 高さ（ピクセル）
        /// </summary>
        public int Height { get; set; }
    }
}
