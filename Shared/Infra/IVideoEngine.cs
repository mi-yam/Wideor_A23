using System;
using System.Threading.Tasks;
using Wideor.App.Shared.Domain;

namespace Wideor.App.Shared.Infra
{
    /// <summary>
    /// 動画再生エンジンの契約インターフェース。
    /// 動画の読み込み、再生、制御を提供します。
    /// </summary>
    public interface IVideoEngine : IDisposable
    {
        /// <summary>
        /// 現在の再生位置（秒）
        /// </summary>
        IObservable<double> CurrentPosition { get; }

        /// <summary>
        /// 動画の総時間（秒）
        /// </summary>
        IObservable<double> TotalDuration { get; }

        /// <summary>
        /// 再生状態（true: 再生中, false: 停止/一時停止）
        /// </summary>
        IObservable<bool> IsPlaying { get; }

        /// <summary>
        /// 動画の読み込み状態
        /// </summary>
        IObservable<bool> IsLoaded { get; }

        /// <summary>
        /// エラーが発生した場合に通知されるストリーム
        /// </summary>
        IObservable<MediaError> Errors { get; }

        /// <summary>
        /// 動画ファイルを読み込みます。
        /// </summary>
        /// <param name="filePath">動画ファイルのパス</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>読み込みが成功した場合true</returns>
        Task<bool> LoadAsync(string filePath, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// 動画の再生を開始します。
        /// </summary>
        void Play();

        /// <summary>
        /// 動画の再生を一時停止します。
        /// </summary>
        void Pause();

        /// <summary>
        /// 動画の再生を停止します。
        /// </summary>
        void Stop();

        /// <summary>
        /// 指定された位置にシークします。
        /// </summary>
        /// <param name="position">シーク先の位置（秒）</param>
        Task SeekAsync(double position);

        /// <summary>
        /// 再生速度を設定します（1.0が通常速度）。
        /// </summary>
        /// <param name="speed">再生速度（0.25～4.0の範囲が推奨）</param>
        void SetPlaybackSpeed(double speed);

        /// <summary>
        /// 音量を設定します（0.0～1.0の範囲）。
        /// </summary>
        /// <param name="volume">音量（0.0: 無音, 1.0: 最大音量）</param>
        void SetVolume(double volume);

        /// <summary>
        /// 現在のフレームを画像として取得します。
        /// </summary>
        /// <returns>現在のフレームの画像データ（該当する場合）</returns>
        Task<System.Windows.Media.Imaging.BitmapSource?> GetCurrentFrameAsync();

        /// <summary>
        /// 動画の情報を取得します。
        /// </summary>
        /// <returns>動画の幅、高さ、フレームレートなどの情報</returns>
        Task<VideoInfo?> GetVideoInfoAsync();
    }

    /// <summary>
    /// 動画の情報を表すレコード
    /// </summary>
    public record VideoInfo
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required double FrameRate { get; init; }
        public required double Duration { get; init; }
        public string? Codec { get; init; }
        public long? Bitrate { get; init; }
    }
}
