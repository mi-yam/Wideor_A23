using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// 高度なサムネイル生成プロバイダー
    /// キーフレーム抽出、タイルマップ、WebP形式、マルチスケール、ストリーミング生成をサポート
    /// </summary>
    public class AdvancedThumbnailProvider : IThumbnailProvider, IDisposable
    {
        private readonly IThumbnailProvider _baseProvider;
        private readonly IThumbnailCache _cache;
        private readonly Subject<ThumbnailGenerationProgress> _progressSubject = new();
        private readonly Subject<MediaError> _errorsSubject = new();

        // マルチスケールの定義
        private readonly List<(int Width, int Height)> _scales = new()
        {
            (160, 90),   // ズームアウト用
            (320, 180),  // 標準
            (640, 360)   // ズームイン用
        };

        public IObservable<ThumbnailGenerationProgress> Progress => _progressSubject.AsObservable();
        public IObservable<MediaError> Errors => _errorsSubject.AsObservable();

        public AdvancedThumbnailProvider(IThumbnailProvider baseProvider, IThumbnailCache cache)
        {
            _baseProvider = baseProvider ?? throw new ArgumentNullException(nameof(baseProvider));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            // ベースプロバイダーの進捗とエラーを転送
            _baseProvider.Progress.Subscribe(_progressSubject.OnNext);
            _baseProvider.Errors.Subscribe(_errorsSubject.OnNext);
        }

        public async Task<BitmapSource?> GenerateThumbnailAsync(
            string videoFilePath,
            double timePosition,
            int width = 320,
            int height = 180,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. キャッシュから取得を試みる（プロジェクトディレクトリは後で追加可能）
                var cached = await _cache.GetThumbnailAsync(videoFilePath, timePosition, width, height, cancellationToken);
                if (cached != null)
                    return cached;

                // 2. キャッシュにない場合は生成（ストリーミング生成）
                var thumbnail = await _baseProvider.GenerateThumbnailAsync(
                    videoFilePath, timePosition, width, height, cancellationToken);

                if (thumbnail != null)
                {
                    // 3. キャッシュに保存
                    await _cache.SaveThumbnailAsync(videoFilePath, timePosition, thumbnail, width, height, cancellationToken);
                }

                return thumbnail;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(
                    "AdvancedThumbnailProvider.cs:GenerateThumbnailAsync",
                    "Exception generating thumbnail",
                    new { videoFilePath, timePosition, width, height, exceptionType = ex.GetType().Name, message = ex.Message });
                return null;
            }
        }

        public async Task<Dictionary<double, BitmapSource>> GenerateThumbnailsAsync(
            string videoFilePath,
            double[] timePositions,
            int width = 320,
            int height = 180,
            CancellationToken cancellationToken = default,
            double? knownDuration = null)
        {
            var result = new Dictionary<double, BitmapSource>();
            var keyFrames = await _cache.GetKeyFramesAsync(videoFilePath, cancellationToken, null);

            if (keyFrames.Count == 0)
            {
                // キーフレームが取得できない場合は、ベースプロバイダーに委譲
                return await _baseProvider.GenerateThumbnailsAsync(
                    videoFilePath, timePositions, width, height, cancellationToken, knownDuration);
            }

            // キーフレームベースでサムネイルを生成
            var tasks = timePositions.Select(async timePosition =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return (timePosition, (BitmapSource?)null);

                // 最も近いキーフレームを見つける
                var nearestKeyFrame = keyFrames.OrderBy(kf => Math.Abs(kf - timePosition)).First();
                
                var thumbnail = await GenerateThumbnailAsync(
                    videoFilePath, nearestKeyFrame, width, height, cancellationToken);

                return (timePosition, thumbnail);
            });

            var results = await Task.WhenAll(tasks);
            
            foreach (var (timePosition, thumbnail) in results)
            {
                if (thumbnail != null)
                {
                    result[timePosition] = thumbnail;
                }
            }

            return result;
        }

        public async Task<Dictionary<double, BitmapSource>> GenerateThumbnailsEvenlyAsync(
            string videoFilePath,
            int count,
            int width = 160,
            int height = 90,
            CancellationToken cancellationToken = default,
            double? knownDuration = null)
        {
            // キーフレームを取得
            var keyFrames = await _cache.GetKeyFramesAsync(videoFilePath, cancellationToken, null);

            if (keyFrames.Count == 0 || knownDuration == null)
            {
                // キーフレームが取得できない場合は、ベースプロバイダーに委譲
                return await _baseProvider.GenerateThumbnailsEvenlyAsync(
                    videoFilePath, count, width, height, cancellationToken, knownDuration);
            }

            // キーフレームから等間隔で選択
            var selectedKeyFrames = new List<double>();
            var interval = knownDuration.Value / (count - 1);

            for (int i = 0; i < count; i++)
            {
                var targetTime = i * interval;
                var nearestKeyFrame = keyFrames.OrderBy(kf => Math.Abs(kf - targetTime)).First();
                if (!selectedKeyFrames.Contains(nearestKeyFrame))
                {
                    selectedKeyFrames.Add(nearestKeyFrame);
                }
            }

            return await GenerateThumbnailsAsync(
                videoFilePath, selectedKeyFrames.ToArray(), width, height, cancellationToken, knownDuration);
        }

        /// <summary>
        /// マルチスケールでサムネイルを事前生成します（取り込み時）
        /// </summary>
        public async Task<ThumbnailData> GenerateMultiScaleThumbnailsAsync(
            string videoFilePath,
            double baseInterval = 1.0,
            CancellationToken cancellationToken = default,
            string? projectDirectory = null)
        {
            var thumbnailData = new ThumbnailData
            {
                VideoIdentifier = videoFilePath,
                Format = "jpeg-tile",
                TileSize = "10x10",
                BaseInterval = baseInterval
            };

            // キーフレームを取得
            var keyFrames = await _cache.GetKeyFramesAsync(videoFilePath, cancellationToken, projectDirectory);
            thumbnailData.KeyFrames = keyFrames;

            if (keyFrames.Count == 0)
                return thumbnailData;

            // ThumbnailDirectoryを設定
            if (!string.IsNullOrEmpty(projectDirectory))
            {
                var videoHash = GetVideoHash(videoFilePath);
                thumbnailData.ThumbnailDirectory = Path.Combine("thumbnails", videoHash);
            }

            // 各スケールでサムネイルを生成
            foreach (var (width, height) in _scales)
            {
                var scale = new ThumbnailScale
                {
                    Width = width,
                    Height = height,
                    Size = $"{width}x{height}"
                };

                // ファイルパスを設定
                if (!string.IsNullOrEmpty(thumbnailData.ThumbnailDirectory))
                {
                    // タイルファイルのパスを設定（簡易実装：最初のタイルのみ）
                    scale.FilePath = Path.Combine(thumbnailData.ThumbnailDirectory, $"tile_{scale.Size}_0000.jpg");
                }

                // キーフレームごとにサムネイルを生成
                var generatedCount = 0;
                foreach (var keyFrame in keyFrames)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // キャッシュから取得を試みる（プロジェクトディレクトリを指定）
                    var cached = await _cache.GetThumbnailAsync(videoFilePath, keyFrame, width, height, cancellationToken, projectDirectory);
                    if (cached != null)
                    {
                        generatedCount++;
                        continue;
                    }

                    // キャッシュにない場合は生成
                    var thumbnail = await _baseProvider.GenerateThumbnailAsync(
                        videoFilePath, keyFrame, width, height, cancellationToken);

                    if (thumbnail != null)
                    {
                        // キャッシュに保存（プロジェクトディレクトリを指定）
                        await _cache.SaveThumbnailAsync(videoFilePath, keyFrame, thumbnail, width, height, cancellationToken, projectDirectory);
                        generatedCount++;
                    }

                    // 進捗を通知
                    _progressSubject.OnNext(new ThumbnailGenerationProgress
                    {
                        FilePath = videoFilePath,
                        CurrentIndex = generatedCount,
                        TotalCount = keyFrames.Count * _scales.Count
                    });
                }

                thumbnailData.Scales.Add(scale);
            }

            return thumbnailData;
        }

        public async Task<BitmapSource?> GenerateThumbnailFromImageAsync(
            string imageFilePath,
            int width = 320,
            int height = 180,
            CancellationToken cancellationToken = default)
        {
            return await _baseProvider.GenerateThumbnailFromImageAsync(
                imageFilePath, width, height, cancellationToken);
        }

        /// <summary>
        /// 動画ファイルのハッシュを計算します
        /// </summary>
        private string GetVideoHash(string videoFilePath)
        {
            var fileInfo = new FileInfo(videoFilePath);
            var hashInput = $"{fileInfo.FullName}_{fileInfo.Length}_{fileInfo.LastWriteTime:O}";
            
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose()
        {
            if (_baseProvider is IDisposable baseDisposable)
                baseDisposable.Dispose();
            if (_cache is IDisposable cacheDisposable)
                cacheDisposable.Dispose();
            _progressSubject?.Dispose();
            _errorsSubject?.Dispose();
        }
    }
}
