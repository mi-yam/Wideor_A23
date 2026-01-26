using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// サムネイルキャッシュの契約インターフェース
    /// </summary>
    public interface IThumbnailCache : IDisposable
    {
        /// <summary>
        /// サムネイルをキャッシュから取得します
        /// </summary>
        Task<BitmapSource?> GetThumbnailAsync(
            string videoFilePath,
            double timePosition,
            int width,
            int height,
            CancellationToken cancellationToken = default,
            string? projectDirectory = null);

        /// <summary>
        /// サムネイルをキャッシュに保存します
        /// </summary>
        Task SaveThumbnailAsync(
            string videoFilePath,
            double timePosition,
            BitmapSource thumbnail,
            int width,
            int height,
            CancellationToken cancellationToken = default,
            string? projectDirectory = null);

        /// <summary>
        /// 動画のキーフレーム位置を取得します
        /// </summary>
        Task<List<double>> GetKeyFramesAsync(
            string videoFilePath,
            CancellationToken cancellationToken = default,
            string? projectDirectory = null);

        /// <summary>
        /// タイルマップからサムネイルを取得します
        /// </summary>
        Task<BitmapSource?> GetThumbnailFromTileAsync(
            string tileFilePath,
            int tileX,
            int tileY,
            int thumbnailWidth,
            int thumbnailHeight,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// タイルマップにサムネイルを保存します
        /// </summary>
        Task SaveThumbnailToTileAsync(
            string tileFilePath,
            int tileX,
            int tileY,
            BitmapSource thumbnail,
            int tileWidth,
            int tileHeight,
            CancellationToken cancellationToken = default);
    }
}
