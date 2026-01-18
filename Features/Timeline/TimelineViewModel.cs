using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Wideor.App.Shared.Domain;
using Wideor.App.Shared.Infra;

namespace Wideor.App.Features.Timeline
{
    /// <summary>
    /// タイムライン機能のViewModel。
    /// スクロール同期、ズーム管理、サムネイル生成を統合します。
    /// </summary>
    public class TimelineViewModel : IDisposable
    {
        private readonly IScrollCoordinator _scrollCoordinator;
        private readonly ITimeRulerService _timeRulerService;
        private readonly IThumbnailProvider _thumbnailProvider;
        private readonly CompositeDisposable _disposables = new();

        // --- Reactive Properties ---

        /// <summary>
        /// 現在のズームレベル（1秒あたりのピクセル数）
        /// </summary>
        public IReadOnlyReactiveProperty<double> PixelsPerSecond { get; }

        /// <summary>
        /// 現在のスクロール位置（0.0～1.0）
        /// </summary>
        public IReadOnlyReactiveProperty<double> ScrollPosition { get; }

        /// <summary>
        /// ScrollCoordinator（ViewからScrollViewerを登録するために公開）
        /// </summary>
        public IScrollCoordinator ScrollCoordinator => _scrollCoordinator;

        /// <summary>
        /// タイムラインの総高さ（ピクセル）
        /// </summary>
        public IReadOnlyReactiveProperty<double> TotalHeight { get; }

        /// <summary>
        /// サムネイルアイテムのコレクション
        /// </summary>
        public ReadOnlyObservableCollection<ThumbnailItem> ThumbnailItems { get; }

        private readonly ObservableCollection<ThumbnailItem> _thumbnailItems = new();

        /// <summary>
        /// 動画ファイルのパス（サムネイル生成用）
        /// </summary>
        public ReactiveProperty<string?> VideoFilePath { get; }

        /// <summary>
        /// 動画の総時間（秒）
        /// </summary>
        public ReactiveProperty<double> TotalDuration { get; }

        /// <summary>
        /// サムネイルの幅（ピクセル）
        /// </summary>
        public ReactiveProperty<int> ThumbnailWidth { get; }

        /// <summary>
        /// サムネイルの高さ（ピクセル）
        /// </summary>
        public ReactiveProperty<int> ThumbnailHeight { get; }

        // --- Commands ---

        /// <summary>
        /// ズームインコマンド
        /// </summary>
        public ReactiveCommand ZoomInCommand { get; }

        /// <summary>
        /// ズームアウトコマンド
        /// </summary>
        public ReactiveCommand ZoomOutCommand { get; }

        /// <summary>
        /// サムネイルを生成するコマンド
        /// </summary>
        public ReactiveCommand GenerateThumbnailsCommand { get; }

        private CancellationTokenSource? _thumbnailCancellation;

        public TimelineViewModel(
            IScrollCoordinator scrollCoordinator,
            ITimeRulerService timeRulerService,
            IThumbnailProvider thumbnailProvider)
        {
            _scrollCoordinator = scrollCoordinator ?? throw new ArgumentNullException(nameof(scrollCoordinator));
            _timeRulerService = timeRulerService ?? throw new ArgumentNullException(nameof(timeRulerService));
            _thumbnailProvider = thumbnailProvider ?? throw new ArgumentNullException(nameof(thumbnailProvider));

            // ITimeRulerServiceのPixelsPerSecondを購読
            PixelsPerSecond = _timeRulerService.PixelsPerSecond
                .ToReadOnlyReactiveProperty(100.0) // デフォルト: 100px/秒
                .AddTo(_disposables);

            // IScrollCoordinatorのスクロール位置を購読
            ScrollPosition = _scrollCoordinator.ScrollPosition
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);

            // サムネイルアイテムのコレクション
            ThumbnailItems = new ReadOnlyObservableCollection<ThumbnailItem>(_thumbnailItems);

            // プロパティの初期化
            VideoFilePath = new ReactiveProperty<string?>()
                .AddTo(_disposables);

            TotalDuration = new ReactiveProperty<double>(0.0)
                .AddTo(_disposables);

            ThumbnailWidth = new ReactiveProperty<int>(160)
                .AddTo(_disposables);

            ThumbnailHeight = new ReactiveProperty<int>(90)
                .AddTo(_disposables);

            // 総高さの計算（TotalDuration × PixelsPerSecond）
            TotalHeight = TotalDuration
                .CombineLatest(PixelsPerSecond, (duration, pps) => duration * pps)
                .ToReadOnlyReactiveProperty(0.0)
                .AddTo(_disposables);

            // スクロール位置が変更されたら、MaxScrollOffsetを更新
            TotalHeight
                .Subscribe(height => _scrollCoordinator.SetMaxScrollOffset(Math.Max(0, height - 100))) // 100pxはマージン
                .AddTo(_disposables);

            // コマンドの初期化
            ZoomInCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    var current = PixelsPerSecond.Value;
                    _timeRulerService.SetZoomLevel(current * 1.5);
                })
                .AddTo(_disposables);

            ZoomOutCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    var current = PixelsPerSecond.Value;
                    _timeRulerService.SetZoomLevel(current / 1.5);
                })
                .AddTo(_disposables);

            GenerateThumbnailsCommand = VideoFilePath
                .Select(path => !string.IsNullOrEmpty(path))
                .ToReactiveCommand()
                .WithSubscribe(() => _ = GenerateThumbnailsAsync())
                .AddTo(_disposables);

            // PixelsPerSecondが変更されたら、サムネイルを再生成
            PixelsPerSecond
                .Skip(1) // 初期値はスキップ
                .Throttle(TimeSpan.FromMilliseconds(500)) // ズーム操作中の頻繁な更新を抑制
                .Subscribe(_ => { GenerateThumbnailsAsync(); })
                .AddTo(_disposables);
        }

        /// <summary>
        /// スクロール位置を設定します。
        /// </summary>
        public void SetScrollPosition(double position)
        {
            _scrollCoordinator.SetScrollPosition(position);
        }

        /// <summary>
        /// スクロールオフセットを設定します。
        /// </summary>
        public void SetScrollOffset(double offset)
        {
            _scrollCoordinator.SetScrollOffset(offset);
        }

        /// <summary>
        /// ズームレベルを設定します。
        /// </summary>
        public void SetZoomLevel(double pixelsPerSecond)
        {
            _timeRulerService.SetZoomLevel(pixelsPerSecond);
        }

        /// <summary>
        /// 時間をY座標に変換します。
        /// </summary>
        public double TimeToY(double time)
        {
            return _timeRulerService.TimeToY(time);
        }

        /// <summary>
        /// Y座標を時間に変換します。
        /// </summary>
        public double YToTime(double y)
        {
            return _timeRulerService.YToTime(y);
        }

        /// <summary>
        /// サムネイルを生成します。
        /// </summary>
        private async Task GenerateThumbnailsAsync()
        {
            if (string.IsNullOrEmpty(VideoFilePath.Value) || TotalDuration.Value <= 0)
            {
                _thumbnailItems.Clear();
                return;
            }

            // 前回の生成をキャンセル
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation = new CancellationTokenSource();
            var cancellationToken = _thumbnailCancellation.Token;

            try
            {
                var pixelsPerSecond = PixelsPerSecond.Value;
                var viewportHeight = 600.0; // 仮のビューポート高さ（実際はViewから取得）

                // 表示可能な範囲を計算
                var visibleTimeRange = viewportHeight / pixelsPerSecond;
                var thumbnailCount = Math.Max(10, (int)(visibleTimeRange / 5.0)); // 5秒間隔でサムネイル生成

                // 等間隔でサムネイルを生成
                var thumbnails = await _thumbnailProvider.GenerateThumbnailsEvenlyAsync(
                    VideoFilePath.Value,
                    thumbnailCount,
                    ThumbnailWidth.Value,
                    ThumbnailHeight.Value,
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return;

                // サムネイルアイテムを更新
                _thumbnailItems.Clear();
                foreach (var kvp in thumbnails.OrderBy(x => x.Key))
                {
                    _thumbnailItems.Add(new ThumbnailItem
                    {
                        TimePosition = kvp.Key,
                        Thumbnail = kvp.Value,
                        YPosition = TimeToY(kvp.Key)
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合は何もしない
            }
            catch (Exception ex)
            {
                // エラーハンドリング（必要に応じてログ出力など）
                System.Diagnostics.Debug.WriteLine($"サムネイル生成エラー: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
            _disposables?.Dispose();
        }
    }

    /// <summary>
    /// サムネイルアイテム（Timeline機能専用）
    /// </summary>
    public class ThumbnailItem
    {
        public double TimePosition { get; set; }
        public System.Windows.Media.Imaging.BitmapSource? Thumbnail { get; set; }
        public double YPosition { get; set; }
    }
}
