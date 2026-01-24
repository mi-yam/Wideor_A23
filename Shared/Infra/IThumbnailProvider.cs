using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// サムネイル生成の契約インターフェース。
    /// 動画や画像からサムネイルを生成します。
    /// </summary>
    public interface IThumbnailProvider
    {
        /// <summary>
        /// 動画ファイルから指定された時間位置のサムネイルを生成します。
        /// </summary>
        /// <param name="videoFilePath">動画ファイルのパス</param>
        /// <param name="timePosition">サムネイルを取得する時間位置（秒）</param>
        /// <param name="width">サムネイルの幅（ピクセル）</param>
        /// <param name="height">サムネイルの高さ（ピクセル）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>生成されたサムネイル画像、失敗した場合はnull</returns>
        Task<System.Windows.Media.Imaging.BitmapSource?> GenerateThumbnailAsync(
            string videoFilePath,
            double timePosition,
            int width = 160,
            int height = 90,
            System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// 動画ファイルから複数のサムネイルを一括生成します。
        /// </summary>
        /// <param name="videoFilePath">動画ファイルのパス</param>
        /// <param name="timePositions">サムネイルを取得する時間位置の配列（秒）</param>
        /// <param name="width">サムネイルの幅（ピクセル）</param>
        /// <param name="height">サムネイルの高さ（ピクセル）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <param name="knownDuration">既知の動画の長さ（秒）。指定された場合、Duration取得処理をスキップします。</param>
        /// <returns>時間位置をキーとするサムネイルの辞書</returns>
        Task<Dictionary<double, System.Windows.Media.Imaging.BitmapSource>> GenerateThumbnailsAsync(
            string videoFilePath,
            double[] timePositions,
            int width = 160,
            int height = 90,
            System.Threading.CancellationToken cancellationToken = default,
            double? knownDuration = null);

        /// <summary>
        /// 動画ファイルから等間隔でサムネイルを生成します。
        /// </summary>
        /// <param name="videoFilePath">動画ファイルのパス</param>
        /// <param name="count">生成するサムネイルの数</param>
        /// <param name="width">サムネイルの幅（ピクセル）</param>
        /// <param name="height">サムネイルの高さ（ピクセル）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <param name="knownDuration">既知の動画の長さ（秒）。指定された場合、Duration取得処理をスキップします。</param>
        /// <returns>時間位置をキーとするサムネイルの辞書</returns>
        Task<Dictionary<double, System.Windows.Media.Imaging.BitmapSource>> GenerateThumbnailsEvenlyAsync(
            string videoFilePath,
            int count,
            int width = 160,
            int height = 90,
            System.Threading.CancellationToken cancellationToken = default,
            double? knownDuration = null);

        /// <summary>
        /// 画像ファイルからサムネイルを生成します。
        /// </summary>
        /// <param name="imageFilePath">画像ファイルのパス</param>
        /// <param name="width">サムネイルの幅（ピクセル）</param>
        /// <param name="height">サムネイルの高さ（ピクセル）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>生成されたサムネイル画像、失敗した場合はnull</returns>
        Task<System.Windows.Media.Imaging.BitmapSource?> GenerateThumbnailFromImageAsync(
            string imageFilePath,
            int width = 160,
            int height = 90,
            System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// サムネイル生成の進捗を通知するストリーム
        /// </summary>
        IObservable<ThumbnailGenerationProgress> Progress { get; }

        /// <summary>
        /// サムネイル生成中のエラーを通知するストリーム
        /// </summary>
        IObservable<MediaError> Errors { get; }
    }

    /// <summary>
    /// サムネイル生成の進捗情報
    /// </summary>
    public record ThumbnailGenerationProgress
    {
        public required string FilePath { get; init; }
        public required int CurrentIndex { get; init; }
        public required int TotalCount { get; init; }
        public double ProgressPercentage => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100 : 0;
    }
}
